//go:build windows

package lifecycle

import (
	"bytes"
	"context"
	"errors"
	"io"
	"net"
	"net/http"
	"net/url"
	"os"
	"os/exec"
	"path/filepath"
	"runtime"
	"slices"
	"strings"
	"sync"
	"syscall"
	"time"
	"unsafe"

	"github.com/Just9120/llm-inspector/internal/resources"
	"github.com/Just9120/llm-inspector/internal/winhost"
	"golang.org/x/sys/windows"
)

type ownedProcess struct {
	identity Identity
	handle   windows.Handle
	job      windows.Handle
	rootPath string
	endpoint string
}

// WindowsRuntime retains native handles, not just PIDs. An unnamed Job Object
// proves descendants of a process assigned while suspended, before any backend
// instruction runs. No kill-on-close, breakaway, broad tree-kill or service API.
type WindowsRuntime struct {
	op     sync.Mutex
	mu     sync.Mutex
	owned  map[uint32]*ownedProcess
	closed bool
	client *http.Client
}

func NewWindowsRuntime() *WindowsRuntime {
	transport := &http.Transport{Proxy: nil, DialContext: (&net.Dialer{Timeout: 3 * time.Second}).DialContext, DisableCompression: true, ResponseHeaderTimeout: 10 * time.Second, MaxResponseHeaderBytes: 16384, MaxConnsPerHost: 2, IdleConnTimeout: 30 * time.Second}
	return &WindowsRuntime{owned: map[uint32]*ownedProcess{}, client: &http.Client{Transport: transport, Timeout: 6 * time.Minute, CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}}
}

func (r *WindowsRuntime) Resolve(ctx context.Context, backend Backend, manual string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	var candidates []string
	if manual != "" {
		candidates = []string{manual}
	} else {
		name := ""
		switch backend {
		case Ollama:
			name = "ollama.exe"
		case LlamaCpp:
			name = "llama-server.exe"
		case LMStudio:
			name = "lms.exe"
		default:
			return "", ErrUnsupported
		}
		if path, err := exec.LookPath(name); err == nil {
			candidates = append(candidates, path)
		}
		if local, err := winhost.LocalDataPath(); err == nil && backend == Ollama {
			candidates = append(candidates, filepath.Join(local, "Programs", "Ollama", name))
		}
		if home, err := windows.KnownFolderPath(windows.FOLDERID_Profile, 0); err == nil && backend == LMStudio {
			candidates = append(candidates, filepath.Join(home, ".lmstudio", "bin", name))
		}
	}
	for _, path := range candidates {
		if !localFile(path, ".exe") {
			continue
		}
		resolved, err := filepath.EvalSymlinks(path)
		if err == nil && localFile(resolved, ".exe") && r.FileExists(resolved) {
			return filepath.Clean(resolved), nil
		}
	}
	return "", ErrTarget
}
func (*WindowsRuntime) FileExists(path string) bool {
	info, err := os.Stat(path)
	return err == nil && info.Mode().IsRegular()
}

type boundedOutput struct {
	buffer   bytes.Buffer
	exceeded bool
}

func (b *boundedOutput) String() string { return b.buffer.String() }

func (b *boundedOutput) Write(p []byte) (int, error) {
	n := len(p)
	remaining := 65536 - b.buffer.Len()
	if n > remaining {
		b.exceeded = true
		p = p[:remaining]
	}
	_, _ = b.buffer.Write(p)
	return n, nil
}
func (r *WindowsRuntime) Execute(ctx context.Context, command Command) (CommandResult, error) {
	if !localFile(command.Executable, ".exe") || command.Timeout <= 0 || command.Timeout > 6*time.Minute {
		return CommandResult{}, ErrCommand
	}
	ctx, cancel := context.WithTimeout(ctx, command.Timeout)
	defer cancel()
	cmd := exec.CommandContext(ctx, command.Executable, command.Arguments...)
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
	cmd.Dir = filepath.Dir(command.Executable)
	cmd.Env = safeEnvironment(command.Environment)
	cmd.WaitDelay = 200 * time.Millisecond
	var stdout, stderr boundedOutput
	cmd.Stdout = &stdout
	cmd.Stderr = &stderr
	err := cmd.Run()
	result := CommandResult{Stdout: stdout.String(), Stderr: stderr.String(), ExitCode: -1}
	if cmd.ProcessState != nil {
		result.ExitCode = cmd.ProcessState.ExitCode()
	}
	if err != nil || stdout.exceeded || stderr.exceeded {
		return CommandResult{ExitCode: result.ExitCode}, ErrCommand
	}
	return result, nil
}

// Native backend defaults must not silently inherit Inspector's runtime flags,
// public bind, remote LMS host or CORS settings from its launching shell.
func safeEnvironment(overrides map[string]string) []string {
	values := map[string]string{}
	for _, pair := range os.Environ() {
		key, value, ok := strings.Cut(pair, "=")
		if !ok || key == "" {
			continue
		}
		upper := strings.ToUpper(key)
		if strings.HasPrefix(upper, "OLLAMA_") || strings.HasPrefix(upper, "LMS_") || strings.HasPrefix(upper, "LLAMA_ARG_") {
			continue
		}
		values[upper] = key + "=" + value
	}
	for key, value := range overrides {
		values[strings.ToUpper(key)] = key + "=" + value
	}
	result := make([]string, 0, len(values))
	for _, pair := range values {
		result = append(result, pair)
	}
	slices.SortFunc(result, func(a, b string) int { return strings.Compare(strings.ToUpper(a), strings.ToUpper(b)) })
	return result
}

func (*WindowsRuntime) Listener(ctx context.Context, endpoint string) (*Identity, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if !validEndpoint(endpoint) {
		return nil, ErrTarget
	}
	owner, err := lookupListener(endpoint)
	if err != nil || owner != nil {
		return owner, err
	}
	// An unavailable owner lookup is not proof of a free port. Binding tests
	// occupancy without connecting to, sending data to, or stopping its owner.
	u, _ := url.Parse(endpoint)
	listener, err := net.Listen("tcp4", u.Host)
	if err != nil {
		return nil, ErrOccupied
	}
	_ = listener.Close()
	return nil, nil
}
func lookupListener(endpoint string, exact ...bool) (*Identity, error) {
	association := (resources.WindowsResolver{}).Resolve(endpoint)
	if len(exact) > 0 && exact[0] {
		association = (resources.WindowsResolver{}).ResolveExactLoopback(endpoint)
	}
	if association != nil {
		value, err := winhost.ProcessIdentity(uint32(association.PID))
		if err != nil || !value.StartedAt.Equal(association.StartedAt) {
			return nil, ErrOwnership
		}
		return &Identity{PID: value.PID, StartedAt: value.StartedAt, ImagePath: value.ImagePath}, nil
	}
	return nil, nil
}

func same(a, b Identity) bool {
	return a.PID != 0 && a.PID == b.PID && a.StartedAt.Equal(b.StartedAt) && strings.EqualFold(filepath.Clean(a.ImagePath), filepath.Clean(b.ImagePath))
}
func identityFor(handle windows.Handle, pid uint32) (Identity, error) {
	p, err := winhost.IdentityForHandle(handle, pid)
	return Identity{PID: p.PID, StartedAt: p.StartedAt, ImagePath: p.ImagePath}, err
}
func aliveHandle(handle windows.Handle, identity Identity) bool {
	status, err := windows.WaitForSingleObject(handle, 0)
	if err != nil || status != uint32(windows.WAIT_TIMEOUT) {
		return false
	}
	actual, err := identityFor(handle, identity.PID)
	return err == nil && same(actual, identity)
}
func (r *WindowsRuntime) Alive(identity Identity) bool {
	r.mu.Lock()
	defer r.mu.Unlock()
	p := r.owned[identity.PID]
	return p != nil && same(p.identity, identity) && aliveHandle(p.handle, identity)
}

func spawnOwned(command Command) (*ownedProcess, error) {
	if !localFile(command.Executable, ".exe") {
		return nil, ErrTarget
	}
	job, err := windows.CreateJobObject(nil, nil)
	if err != nil {
		return nil, ErrOwnership
	}
	app, err := windows.UTF16PtrFromString(command.Executable)
	if err != nil {
		windows.CloseHandle(job)
		return nil, ErrCommand
	}
	args := []string{syscall.EscapeArg(command.Executable)}
	for _, arg := range command.Arguments {
		args = append(args, syscall.EscapeArg(arg))
	}
	line, err := windows.UTF16PtrFromString(strings.Join(args, " "))
	if err != nil {
		windows.CloseHandle(job)
		return nil, ErrCommand
	}
	dir, _ := windows.UTF16PtrFromString(filepath.Dir(command.Executable))
	// UTF16FromString rejects embedded NULs. Encode each environment entry
	// individually and terminate the block with an additional NUL instead.
	block := []uint16{}
	for _, pair := range safeEnvironment(command.Environment) {
		encoded, e := windows.UTF16FromString(pair)
		if e != nil {
			windows.CloseHandle(job)
			return nil, ErrCommand
		}
		block = append(block, encoded...)
	}
	block = append(block, 0)
	si := windows.StartupInfo{Cb: uint32(unsafe.Sizeof(windows.StartupInfo{}))}
	var info windows.ProcessInformation
	err = windows.CreateProcess(app, line, nil, nil, false, windows.CREATE_SUSPENDED|windows.CREATE_NO_WINDOW|windows.CREATE_UNICODE_ENVIRONMENT, &block[0], dir, &si, &info)
	runtime.KeepAlive(block)
	if err != nil {
		windows.CloseHandle(job)
		return nil, ErrCommand
	}
	defer windows.CloseHandle(info.Thread)
	cleanup := func() {
		_ = windows.TerminateProcess(info.Process, 1)
		windows.CloseHandle(info.Process)
		windows.CloseHandle(job)
	}
	identity, err := identityFor(info.Process, info.ProcessId)
	if err != nil || !strings.EqualFold(identity.ImagePath, command.Executable) {
		cleanup()
		return nil, ErrOwnership
	}
	if err = windows.AssignProcessToJobObject(job, info.Process); err != nil {
		cleanup()
		return nil, ErrOwnership
	}
	if _, err = windows.ResumeThread(info.Thread); err != nil {
		cleanup()
		return nil, ErrCommand
	}
	return &ownedProcess{identity: identity, handle: info.Process, job: job, rootPath: command.Executable}, nil
}

var isProcessInJob = windows.NewLazySystemDLL("kernel32.dll").NewProc("IsProcessInJob")

func belongsToJob(handle, job windows.Handle) bool {
	var result int32
	ok, _, _ := isProcessInJob.Call(uintptr(handle), uintptr(job), uintptr(unsafe.Pointer(&result)))
	return ok != 0 && result != 0
}

func (r *WindowsRuntime) Start(ctx context.Context, plan StartPlan) (*Identity, error) {
	r.op.Lock()
	defer r.op.Unlock()
	if !validEndpoint(plan.Endpoint) {
		return nil, ErrTarget
	}
	if owner, err := r.Listener(ctx, plan.Endpoint); err != nil {
		return nil, err
	} else if owner != nil {
		return nil, ErrOccupied
	}
	// A detached CLI can route server start into an already running GUI/daemon.
	// Such a process is external even when the selected HTTP port is still free.
	if plan.Detached {
		occupied, err := imagesRunning(plan.AllowedImages)
		if err != nil || occupied {
			return nil, ErrOwnership
		}
	}
	r.mu.Lock()
	if r.closed {
		r.mu.Unlock()
		return nil, ErrOwnership
	}
	process, err := spawnOwned(plan.Command)
	if err != nil {
		r.mu.Unlock()
		return nil, err
	}
	process.endpoint = plan.Endpoint
	r.owned[process.identity.PID] = process
	root := process.identity
	r.mu.Unlock()
	ctx, cancel := context.WithTimeout(ctx, 30*time.Second)
	defer cancel()
	ticker := time.NewTicker(100 * time.Millisecond)
	defer ticker.Stop()
	for {
		owner, listenerErr := lookupListener(plan.Endpoint, true)
		if listenerErr == nil && owner != nil {
			r.mu.Lock()
			h, openErr := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.PROCESS_TERMINATE|windows.SYNCHRONIZE, false, owner.PID)
			allowed := slices.ContainsFunc(plan.AllowedImages, func(name string) bool { return strings.EqualFold(name, filepath.Base(owner.ImagePath)) })
			if openErr == nil && allowed && aliveHandle(h, *owner) && belongsToJob(h, process.job) && (plan.Detached || same(*owner, root)) {
				if owner.PID != root.PID {
					delete(r.owned, root.PID)
					windows.CloseHandle(process.handle)
					process.handle = h
					process.identity = *owner
					r.owned[owner.PID] = process
				} else {
					windows.CloseHandle(h)
				}
				r.mu.Unlock()
				return owner, nil
			}
			if openErr == nil {
				windows.CloseHandle(h)
			}
			r.mu.Unlock()
			return &root, ErrOwnership
		}
		if !plan.Detached && !r.Alive(root) {
			return &root, ErrReadiness
		}
		select {
		case <-ctx.Done():
			return &root, ErrReadiness
		case <-ticker.C:
		}
	}
}

func imagesRunning(names []string) (bool, error) {
	h, err := windows.CreateToolhelp32Snapshot(windows.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return false, err
	}
	defer windows.CloseHandle(h)
	entry := windows.ProcessEntry32{Size: uint32(unsafe.Sizeof(windows.ProcessEntry32{}))}
	for err = windows.Process32First(h, &entry); err == nil; err = windows.Process32Next(h, &entry) {
		name := windows.UTF16ToString(entry.ExeFile[:])
		if slices.ContainsFunc(names, func(v string) bool { return strings.EqualFold(v, name) }) {
			return true, nil
		}
	}
	if !errors.Is(err, windows.ERROR_NO_MORE_FILES) {
		return false, err
	}
	return false, nil
}

var (
	userDLL           = windows.NewLazySystemDLL("user32.dll")
	enumWindows       = userDLL.NewProc("EnumWindows")
	windowPID         = userDLL.NewProc("GetWindowThreadProcessId")
	postWindowMessage = userDLL.NewProc("PostMessageW")
	closeOwnedWindow  = windows.NewCallback(func(hwnd, pid uintptr) uintptr {
		var actual uint32
		windowPID.Call(hwnd, uintptr(unsafe.Pointer(&actual)))
		if actual == uint32(pid) {
			postWindowMessage.Call(hwnd, 0x10, 0, 0)
		}
		return 1
	})
)

func waitProcess(ctx context.Context, handle windows.Handle, duration time.Duration) bool {
	deadline := time.NewTimer(duration)
	defer deadline.Stop()
	tick := time.NewTicker(50 * time.Millisecond)
	defer tick.Stop()
	for {
		status, err := windows.WaitForSingleObject(handle, 0)
		if err == nil && status == windows.WAIT_OBJECT_0 {
			return true
		}
		select {
		case <-ctx.Done():
			return false
		case <-deadline.C:
			return false
		case <-tick.C:
		}
	}
}
func (r *WindowsRuntime) Stop(ctx context.Context, identity Identity, official *Command) error {
	r.op.Lock()
	defer r.op.Unlock()
	r.mu.Lock()
	defer r.mu.Unlock()
	p := r.owned[identity.PID]
	if p == nil || !same(p.identity, identity) {
		return ErrOwnership
	}
	if aliveHandle(p.handle, identity) {
		// Official stop is safe only while the endpoint still has this exact
		// owned listener. Never send a global CLI stop to a replacement server.
		if official != nil && strings.EqualFold(official.Executable, p.rootPath) {
			owner, err := r.Listener(ctx, p.endpoint)
			if err == nil && owner != nil && same(*owner, identity) {
				_, _ = r.Execute(ctx, *official)
				_ = waitProcess(ctx, p.handle, 5*time.Second)
			}
		}
		if aliveHandle(p.handle, identity) {
			enumWindows.Call(closeOwnedWindow, uintptr(identity.PID))
			_ = waitProcess(ctx, p.handle, 5*time.Second)
		}
		if aliveHandle(p.handle, identity) {
			if err := ctx.Err(); err != nil {
				return err
			}
			// Re-read exact identity on the retained handle immediately before force.
			if !aliveHandle(p.handle, identity) || !belongsToJob(p.handle, p.job) {
				return ErrOwnership
			}
			if err := windows.TerminateProcess(p.handle, 1); err != nil {
				return ErrOwnership
			}
			if !waitProcess(ctx, p.handle, 10*time.Second) {
				return ErrOwnership
			}
		}
	}
	// Descendants can outlive their listener. Enumerate bounded job members,
	// hold each handle and recheck its exact creation/path identity before an
	// individual force stop. Never call TerminateJobObject or kill a PID tree.
	if err := stopJobMembers(ctx, p.job); err != nil {
		return err
	}
	windows.CloseHandle(p.handle)
	windows.CloseHandle(p.job)
	delete(r.owned, identity.PID)
	return nil
}

func stopJobMembers(ctx context.Context, job windows.Handle) error {
	for attempt := 0; attempt < 8; attempt++ {
		var list struct {
			Assigned uint32
			Count    uint32
			PIDs     [256]uintptr
		}
		err := windows.QueryInformationJobObject(job, windows.JobObjectBasicProcessIdList, uintptr(unsafe.Pointer(&list)), uint32(unsafe.Sizeof(list)), nil)
		if err != nil || list.Count > 256 {
			return ErrOwnership
		}
		if list.Count == 0 {
			return nil
		}
		for _, pid := range list.PIDs[:list.Count] {
			if err := ctx.Err(); err != nil {
				return err
			}
			h, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.PROCESS_TERMINATE|windows.SYNCHRONIZE, false, uint32(pid))
			if errors.Is(err, windows.ERROR_INVALID_PARAMETER) {
				continue
			}
			if err != nil {
				return ErrOwnership
			}
			id, err := identityFor(h, uint32(pid))
			if err != nil || !belongsToJob(h, job) {
				windows.CloseHandle(h)
				return ErrOwnership
			}
			if aliveHandle(h, id) {
				enumWindows.Call(closeOwnedWindow, uintptr(id.PID))
				_ = waitProcess(ctx, h, 250*time.Millisecond)
			}
			if aliveHandle(h, id) {
				if ctx.Err() != nil {
					windows.CloseHandle(h)
					return ctx.Err()
				}
				err = windows.TerminateProcess(h, 1)
				if err == nil && !waitProcess(ctx, h, time.Second) {
					err = ErrOwnership
				}
			}
			windows.CloseHandle(h)
			if err != nil {
				return ErrOwnership
			}
		}
	}
	return ErrOwnership
}

func (r *WindowsRuntime) HTTP(ctx context.Context, method, raw string, body []byte) ([]byte, error) {
	u, err := url.Parse(raw)
	if err != nil || u.Scheme != "http" || u.Hostname() != "127.0.0.1" || u.Port() == "" || u.User != nil || u.RawQuery != "" || u.Fragment != "" {
		return nil, ErrTarget
	}
	allowed := method == "GET" && slices.Contains([]string{"/api/tags", "/api/ps", "/health", "/v1/models"}, u.Path) || method == "POST" && u.Path == "/api/generate"
	if !allowed || len(body) > 4096 {
		return nil, ErrUnsupported
	}
	req, err := http.NewRequestWithContext(ctx, method, raw, bytes.NewReader(body))
	if err != nil {
		return nil, ErrCommand
	}
	if body != nil {
		req.Header.Set("Content-Type", "application/json")
	}
	response, err := r.client.Do(req)
	if err != nil {
		return nil, ErrCommand
	}
	defer response.Body.Close()
	if response.StatusCode < 200 || response.StatusCode >= 300 {
		return nil, ErrCommand
	}
	data, err := io.ReadAll(io.LimitReader(response.Body, 65537))
	if err != nil || len(data) > 65536 {
		return nil, ErrCommand
	}
	return data, nil
}

// Exit relinquishes handles, not backend lifetime; closing Inspector must not
// silently stop a server. A subsequent instance observes it as external.
func (r *WindowsRuntime) Close() {
	r.op.Lock()
	defer r.op.Unlock()
	r.mu.Lock()
	defer r.mu.Unlock()
	r.closed = true
	for pid, p := range r.owned {
		windows.CloseHandle(p.handle)
		windows.CloseHandle(p.job)
		delete(r.owned, pid)
	}
	r.client.CloseIdleConnections()
}
