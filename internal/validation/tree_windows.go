//go:build windows

package validation

import (
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"io"
	"os"
	"path/filepath"
	"time"
	"unsafe"

	"github.com/Just9120/llm-inspector/internal/winhost"
	"golang.org/x/sys/windows"
)

var memoryAPI = windows.NewLazySystemDLL("psapi.dll").NewProc("GetProcessMemoryInfo")
var ioAPI = windows.NewLazySystemDLL("kernel32.dll").NewProc("GetProcessIoCounters")

type memoryCounters struct {
	Size, PageFaults                                                                                                           uint32
	PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool, PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile, PrivateUsage uintptr
}
type ioCounters struct{ ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes uint64 }

type retainedProcess struct {
	handle   windows.Handle
	identity winhost.Identity
}

// ProcessTree holds read/query handles. It never launches, kills, assigns a job,
// reads command lines, or opens process memory. It is not a workload controller.
type ProcessTree struct {
	root      uint32
	processes map[uint32]retainedProcess
	closed    bool
}

func AttachProcessTree(pid uint32, executable, expectedSHA string) (*ProcessTree, error) {
	if pid == 0 || !filepath.IsAbs(executable) || !hexID(expectedSHA, 64) {
		return nil, errors.New("invalid_target")
	}
	p, err := openProcess(pid)
	if err != nil {
		return nil, errors.New("target_unavailable")
	}
	valid := false
	defer func() {
		if !valid {
			windows.CloseHandle(p.handle)
		}
	}()
	want, err := os.Stat(executable)
	if err != nil {
		return nil, errors.New("artifact_unavailable")
	}
	actual, err := os.Stat(p.identity.ImagePath)
	if err != nil || !os.SameFile(want, actual) || !want.Mode().IsRegular() || want.Size() > 256*1024*1024 {
		return nil, errors.New("target_identity_mismatch")
	}
	f, err := os.Open(executable)
	if err != nil {
		return nil, errors.New("artifact_unavailable")
	}
	h := sha256.New()
	_, readErr := io.Copy(h, io.LimitReader(f, 256*1024*1024+1))
	closeErr := f.Close()
	if readErr != nil || closeErr != nil || hex.EncodeToString(h.Sum(nil)) != expectedSHA {
		return nil, errors.New("artifact_hash_mismatch")
	}
	valid = true
	return &ProcessTree{root: pid, processes: map[uint32]retainedProcess{pid: p}}, nil
}

func openProcess(pid uint32) (retainedProcess, error) {
	h, err := windows.OpenProcess(windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.SYNCHRONIZE, false, pid)
	if err != nil {
		return retainedProcess{}, err
	}
	identity, err := winhost.IdentityForHandle(h, pid)
	if err != nil {
		windows.CloseHandle(h)
		return retainedProcess{}, err
	}
	return retainedProcess{h, identity}, nil
}

func alive(h windows.Handle) (bool, error) {
	status, err := windows.WaitForSingleObject(h, 0)
	if err != nil {
		return false, err
	}
	if status != uint32(windows.WAIT_TIMEOUT) && status != windows.WAIT_OBJECT_0 {
		return false, errors.New("unknown_process_state")
	}
	return status == uint32(windows.WAIT_TIMEOUT), nil
}

func (t *ProcessTree) Read() (Counters, error) {
	if t.closed {
		return Counters{}, errors.New("capture_closed")
	}
	rootAlive, err := alive(t.processes[t.root].handle)
	if err != nil || !rootAlive {
		return Counters{}, errors.New("target_exited")
	}
	if err := t.discover(); err != nil {
		return Counters{}, err
	}
	result := Counters{Retained: len(t.processes)}
	for _, p := range t.processes {
		var creation, exit, kernel, user windows.Filetime
		if err := windows.GetProcessTimes(p.handle, &creation, &exit, &kernel, &user); err != nil {
			return Counters{}, errors.New("process_times_unavailable")
		}
		var counters ioCounters
		if ok, _, _ := ioAPI.Call(uintptr(p.handle), uintptr(unsafe.Pointer(&counters))); ok == 0 {
			return Counters{}, errors.New("process_io_unavailable")
		}
		result.CPU100ns += (uint64(kernel.HighDateTime)<<32 | uint64(kernel.LowDateTime)) + (uint64(user.HighDateTime)<<32 | uint64(user.LowDateTime))
		result.IOWriteBytes += counters.WriteBytes
		live, err := alive(p.handle)
		if err != nil {
			return Counters{}, errors.New("process_state_unavailable")
		}
		if !live {
			continue
		}
		var m memoryCounters
		m.Size = uint32(unsafe.Sizeof(m))
		if ok, _, _ := memoryAPI.Call(uintptr(p.handle), uintptr(unsafe.Pointer(&m)), uintptr(m.Size)); ok == 0 {
			return Counters{}, errors.New("process_memory_unavailable")
		}
		result.PrivateBytes += uint64(m.PrivateUsage)
		result.Live++
	}
	// Do not report a successful sample if the selected root exited mid-read.
	rootAlive, err = alive(t.processes[t.root].handle)
	if err != nil || !rootAlive {
		return Counters{}, errors.New("target_exited")
	}
	return result, nil
}

func (t *ProcessTree) discover() error {
	snapshot, err := windows.CreateToolhelp32Snapshot(windows.TH32CS_SNAPPROCESS, 0)
	if err != nil {
		return errors.New("process_snapshot_unavailable")
	}
	defer windows.CloseHandle(snapshot)
	entry := windows.ProcessEntry32{Size: uint32(unsafe.Sizeof(windows.ProcessEntry32{}))}
	parents := map[uint32]uint32{}
	for err = windows.Process32First(snapshot, &entry); err == nil; err = windows.Process32Next(snapshot, &entry) {
		if len(parents) >= 65536 {
			return errors.New("process_snapshot_capacity")
		}
		parents[entry.ProcessID] = entry.ParentProcessID
	}
	if !errors.Is(err, windows.ERROR_NO_MORE_FILES) {
		return errors.New("process_snapshot_incomplete")
	}
	for pass := 0; pass < 128; pass++ {
		added := false
		for pid, parent := range parents {
			if _, retained := t.processes[pid]; retained {
				continue
			}
			p, ok := t.processes[parent]
			if !ok {
				continue
			}
			if len(t.processes) >= 512 {
				return errors.New("process_tree_capacity")
			}
			child, err := openProcess(pid)
			if err != nil {
				return errors.New("descendant_unavailable")
			}
			if !validChild(p.identity.StartedAt, child.identity.StartedAt) {
				windows.CloseHandle(child.handle)
				return errors.New("descendant_identity_ambiguous")
			}
			t.processes[pid] = child
			added = true
		}
		if !added {
			return nil
		}
	}
	return errors.New("process_tree_depth")
}

func validChild(parentStart, childStart time.Time) bool {
	return !parentStart.IsZero() && !childStart.IsZero() && !childStart.Before(parentStart)
}

func (t *ProcessTree) Close() {
	if t.closed {
		return
	}
	t.closed = true
	for _, p := range t.processes {
		windows.CloseHandle(p.handle)
	}
}
