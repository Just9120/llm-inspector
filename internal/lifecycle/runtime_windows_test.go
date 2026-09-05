//go:build windows

package lifecycle

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"testing"
	"time"

	"golang.org/x/sys/windows"
)

// Invoked only in our own test executable with a task-specific environment.
// Never starts/stops an installed backend or writes real user configuration.
func TestLifecycleNativeHelper(t *testing.T) {
	mode := os.Getenv("LLM_INSPECTOR_LIFECYCLE_HELPER")
	if mode == "" {
		return
	}
	switch mode {
	case "output":
		fmt.Print(strings.Repeat("x", 131072))
		os.Exit(0)
	case "version":
		fmt.Print("test runtime 1.0")
		os.Exit(0)
	case "child":
		exe, _ := os.Executable()
		cmd := exec.Command(exe, "-test.run=^TestLifecycleNativeHelper$")
		cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true}
		cmd.Env = append(withoutHelper(os.Environ()), "LLM_INSPECTOR_LIFECYCLE_HELPER=listener")
		if cmd.Start() != nil {
			os.Exit(2)
		}
		os.Exit(0)
	case "listener":
		listener, err := net.Listen("tcp4", os.Getenv("LLM_INSPECTOR_LIFECYCLE_ADDRESS"))
		if err != nil {
			os.Exit(3)
		}
		_ = http.Serve(listener, http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { fmt.Fprint(w, `{"status":"ok"}`) }))
		os.Exit(0)
	case "wait":
		time.Sleep(40 * time.Second)
		os.Exit(0)
	default:
		os.Exit(4)
	}
}
func withoutHelper(values []string) []string {
	out := []string{}
	for _, v := range values {
		if !strings.HasPrefix(v, "LLM_INSPECTOR_LIFECYCLE_HELPER=") {
			out = append(out, v)
		}
	}
	return out
}
func helperCommand(t *testing.T, mode string) Command {
	t.Helper()
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	return Command{Executable: exe, Arguments: []string{"-test.run=^TestLifecycleNativeHelper$"}, Environment: map[string]string{"LLM_INSPECTOR_LIFECYCLE_HELPER": mode}, Timeout: 5 * time.Second}
}

func TestNativeExecuteBoundsAndEnvironmentDefaults(t *testing.T) {
	r := NewWindowsRuntime()
	defer r.Close()
	result, err := r.Execute(context.Background(), helperCommand(t, "version"))
	if err != nil || result.Stdout != "test runtime 1.0" {
		t.Fatal(result, err)
	}
	result, err = r.Execute(context.Background(), helperCommand(t, "output"))
	if !errors.Is(err, ErrCommand) || result.Stdout != "" {
		t.Fatal("unbounded output leaked", err)
	}
	command := helperCommand(t, "wait")
	command.Timeout = 50 * time.Millisecond
	if _, err = r.Execute(context.Background(), command); !errors.Is(err, ErrCommand) {
		t.Fatal("timeout", err)
	}
	t.Setenv("OLLAMA_HOST", "0.0.0.0:1234")
	t.Setenv("OLLAMA_ORIGINS", "*")
	t.Setenv("LLAMA_ARG_HOST", "0.0.0.0")
	t.Setenv("LMS_SERVER_HOST", "remote")
	t.Setenv("LLM_INSPECTOR_TEST_KEPT", "yes")
	env := strings.Join(safeEnvironment(map[string]string{"OLLAMA_HOST": "127.0.0.1:11434"}), "\n")
	if strings.Contains(env, "0.0.0.0") || strings.Contains(env, "OLLAMA_ORIGINS=") || strings.Contains(env, "LMS_SERVER_HOST=") || !strings.Contains(env, "LLM_INSPECTOR_TEST_KEPT=yes") || !strings.Contains(env, "OLLAMA_HOST=127.0.0.1:11434") {
		t.Fatal("inherited runtime settings")
	}
}

func TestNativeHTTPBoundsNoRedirectNoProxy(t *testing.T) {
	r := NewWindowsRuntime()
	defer r.Close()
	mode := "ok"
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
		switch mode {
		case "redirect":
			http.Redirect(w, req, "http://example.invalid/", 302)
		case "large":
			fmt.Fprint(w, strings.Repeat("a", 65537))
		case "status":
			w.WriteHeader(500)
		default:
			fmt.Fprint(w, `{"models":[]}`)
		}
	}))
	defer server.Close()
	t.Setenv("HTTP_PROXY", "http://127.0.0.1:1")
	if _, err := r.HTTP(context.Background(), "GET", server.URL+"/api/tags", nil); err != nil {
		t.Fatal(err)
	}
	for _, value := range []string{"redirect", "large", "status"} {
		mode = value
		if _, err := r.HTTP(context.Background(), "GET", server.URL+"/api/tags", nil); err == nil {
			t.Fatal(value)
		}
	}
	for _, path := range []string{"http://example.invalid:1234/api/tags", "http://localhost:1234/api/tags", server.URL + "/api/pull", server.URL + "/api/tags?secret=x", "http://u:p@127.0.0.1:1234/api/tags"} {
		if _, err := r.HTTP(context.Background(), "GET", path, nil); err == nil {
			t.Fatal("unsafe HTTP path")
		}
	}
}

func TestNativeOwnershipExactHandleAndNoExternalStop(t *testing.T) {
	r := NewWindowsRuntime()
	t.Cleanup(r.Close)
	process, err := spawnOwned(helperCommand(t, "wait"))
	if err != nil {
		t.Fatal(err)
	}
	r.owned[process.identity.PID] = process
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		if r.Alive(process.identity) {
			if err := r.Stop(ctx, process.identity, nil); err != nil {
				t.Error(err)
			}
		}
	})
	for _, mutate := range []func(*Identity){func(id *Identity) { id.PID++ }, func(id *Identity) { id.StartedAt = id.StartedAt.Add(time.Second) }, func(id *Identity) { id.ImagePath += ".wrong" }} {
		id := process.identity
		mutate(&id)
		if r.Alive(id) || !errors.Is(r.Stop(context.Background(), id, nil), ErrOwnership) || !r.Alive(process.identity) {
			t.Fatal("PID-only ownership")
		}
	}
	if !belongsToJob(process.handle, process.job) || belongsToJob(windows.CurrentProcess(), process.job) {
		t.Fatal("job ownership")
	}
	// Close releases handles without stopping the backend. Retain a separate
	// handle solely so this test can clean up its own synthetic process.
	h, err := windows.OpenProcess(windows.PROCESS_TERMINATE|windows.PROCESS_QUERY_LIMITED_INFORMATION|windows.SYNCHRONIZE, false, process.identity.PID)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.CloseHandle(h)
	r.Close()
	if !aliveHandle(h, process.identity) {
		t.Fatal("Close killed runtime")
	}
	if err = windows.TerminateProcess(h, 0); err != nil {
		t.Fatal(err)
	}
	if !waitProcess(context.Background(), h, time.Second) {
		t.Fatal("helper cleanup")
	}
}

func TestNativeManagedListenerAndDetachedJobProof(t *testing.T) {
	for _, detached := range []bool{false, true} {
		t.Run(fmt.Sprint(detached), func(t *testing.T) {
			r := NewWindowsRuntime()
			t.Cleanup(r.Close)
			command := helperCommand(t, "listener")
			if detached {
				// Distinct synthetic image avoids colliding with the parent test runner
				// in the pre-existing GUI/daemon observation-only guard.
				copyPath := filepath.Join(t.TempDir(), "inspector-owned-fixture.exe")
				src, err := os.Open(command.Executable)
				if err != nil {
					t.Fatal(err)
				}
				dst, err := os.Create(copyPath)
				if err != nil {
					src.Close()
					t.Fatal(err)
				}
				_, err = io.Copy(dst, src)
				src.Close()
				dst.Close()
				if err != nil {
					t.Fatal(err)
				}
				command.Executable = copyPath
				command.Environment["LLM_INSPECTOR_LIFECYCLE_HELPER"] = "child"
			}
			listener, err := net.Listen("tcp4", "127.0.0.1:0")
			if err != nil {
				t.Fatal(err)
			}
			address := listener.Addr().String()
			listener.Close()
			command.Environment["LLM_INSPECTOR_LIFECYCLE_ADDRESS"] = address
			plan := StartPlan{Command: command, Endpoint: "http://" + address + "/", Detached: detached, AllowedImages: []string{filepath.Base(command.Executable)}}
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			id, err := r.Start(ctx, plan)
			if id != nil {
				t.Cleanup(func() {
					if id == nil {
						return
					}
					cleanup, done := context.WithTimeout(context.Background(), 15*time.Second)
					defer done()
					if err := r.Stop(cleanup, *id, nil); err != nil {
						t.Error("helper cleanup", err)
					}
				})
			}
			if err != nil || id == nil || !r.Alive(*id) {
				t.Fatal(id, err)
			}
			owner, err := r.Listener(ctx, plan.Endpoint)
			if err != nil || owner == nil || !same(*owner, *id) {
				t.Fatal(owner, err)
			}
			if _, err = r.Start(ctx, plan); !errors.Is(err, ErrOccupied) {
				t.Fatal("duplicate start", err)
			}
			cleanup, done := context.WithTimeout(context.Background(), 15*time.Second)
			defer done()
			if err = r.Stop(cleanup, *id, nil); err != nil {
				t.Fatal(err)
			}
			// Cleanup callback is idempotent at the test level, not a PID-only stop.
			id = nil
		})
	}
}

func TestNativeDiscoveryRejectsUnsafeTargets(t *testing.T) {
	r := NewWindowsRuntime()
	defer r.Close()
	for _, path := range []string{`\\server\share\ollama.exe`, `relative.exe`, "https://host/ollama.exe", filepath.Join(t.TempDir(), "missing.exe")} {
		if _, err := r.Resolve(context.Background(), Ollama, path); err == nil {
			t.Fatal(path)
		}
	}
	command := helperCommand(t, "version")
	resolved, err := r.Resolve(context.Background(), Ollama, command.Executable)
	if err != nil || !strings.EqualFold(resolved, command.Executable) {
		t.Fatal(resolved, err)
	}
	if occupied, err := imagesRunning([]string{filepath.Base(command.Executable)}); err != nil || !occupied {
		t.Fatal("existing process guard", occupied, err)
	}
}
