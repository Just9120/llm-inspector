package lifecycle

import (
	"context"
	"encoding/json"
	"errors"
	"path/filepath"
	"reflect"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

type fakeRuntime struct {
	path                string
	version             string
	alive               bool
	occupied            *Identity
	starts              []StartPlan
	stops               []Identity
	commands            []Command
	httpPaths           []string
	startErr, errorStop error
	ready               bool
	installed, loaded   string
	identity            Identity
}

func fakeFor(t *testing.T, backend Backend) (*Manager, *fakeRuntime, *atomic.Int64) {
	t.Helper()
	name := "ollama.exe"
	version := "ollama version is 0.33.2"
	if backend == LlamaCpp {
		name = "llama-server.exe"
		version = "version: b10516"
	}
	if backend == LMStudio {
		name = "lms.exe"
		version = "lms 0.0.47"
	}
	rt := &fakeRuntime{path: filepath.Join(t.TempDir(), name), version: version, ready: true, installed: "model:exact", loaded: "model:exact", identity: Identity{PID: 812, StartedAt: time.Now().UTC()}}
	rt.identity.ImagePath = rt.path
	active := &atomic.Int64{}
	m, err := NewManager(backend, rt, func() int { return int(active.Load()) })
	if err != nil {
		t.Fatal(err)
	}
	return m, rt, active
}
func (f *fakeRuntime) Resolve(context.Context, Backend, string) (string, error) { return f.path, nil }
func (f *fakeRuntime) Execute(_ context.Context, c Command) (CommandResult, error) {
	f.commands = append(f.commands, c)
	if reflect.DeepEqual(c.Arguments, []string{"--version"}) {
		return CommandResult{Stdout: f.version}, nil
	}
	if len(c.Arguments) > 0 && (c.Arguments[0] == "ls" || c.Arguments[0] == "ps") {
		model := f.installed
		if c.Arguments[0] == "ps" {
			model = f.loaded
		}
		b, _ := json.Marshal([]map[string]string{{"modelKey": model, "identifier": model}})
		return CommandResult{Stdout: string(b)}, nil
	}
	return CommandResult{}, nil
}
func (f *fakeRuntime) Listener(context.Context, string) (*Identity, error) {
	if f.occupied == nil && f.alive {
		id := f.identity
		return &id, nil
	}
	return f.occupied, nil
}
func (f *fakeRuntime) Start(_ context.Context, p StartPlan) (*Identity, error) {
	f.starts = append(f.starts, p)
	f.alive = true
	id := f.identity
	return &id, f.startErr
}
func (f *fakeRuntime) Alive(id Identity) bool { return f.alive && id == f.identity }
func (f *fakeRuntime) Stop(_ context.Context, id Identity, _ *Command) error {
	f.stops = append(f.stops, id)
	if f.errorStop != nil {
		return f.errorStop
	}
	f.alive = false
	return nil
}
func (f *fakeRuntime) FileExists(string) bool { return true }
func (f *fakeRuntime) HTTP(_ context.Context, method, url string, _ []byte) ([]byte, error) {
	f.httpPaths = append(f.httpPaths, method+" "+url)
	if !f.ready {
		return nil, ErrReadiness
	}
	if strings.HasSuffix(url, "health") {
		return []byte(`{"status":"ok"}`), nil
	}
	model := f.installed
	if strings.HasSuffix(url, "api/ps") {
		model = f.loaded
	}
	if strings.HasSuffix(url, "v1/models") {
		b, _ := json.Marshal(map[string]any{"data": []map[string]string{{"id": f.loaded}}})
		return b, nil
	}
	b, _ := json.Marshal(map[string]any{"models": []map[string]string{{"name": model}}})
	return b, nil
}
func confirm(t *testing.T, m *Manager) {
	t.Helper()
	s, err := m.Discover(context.Background(), "")
	if err != nil {
		t.Fatal(err)
	}
	if err = m.Confirm(s.Target.ConfirmationToken); err != nil {
		t.Fatal(err)
	}
}

func TestConfirmationParametersAndDetachedSnapshots(t *testing.T) {
	m, rt, _ := fakeFor(t, Ollama)
	if !errors.Is(m.Start(context.Background()), ErrTarget) {
		t.Fatal("unconfirmed start")
	}
	s, err := m.Discover(context.Background(), "")
	if err != nil {
		t.Fatal(err)
	}
	if s.State != PendingConfirmation || s.Confirmed || s.Target.Executable != rt.path {
		t.Fatal(s)
	}
	if !errors.Is(m.Confirm("wrong"), ErrTarget) {
		t.Fatal("wrong token")
	}
	if err = m.Confirm(s.Target.ConfirmationToken); err != nil {
		t.Fatal(err)
	}
	if err = m.SetParameter("local-port", "23456"); err != nil {
		t.Fatal(err)
	}
	next := m.Snapshot()
	if next.Confirmed || next.Target.Endpoint != "http://127.0.0.1:23456/" || next.Target.ConfirmationToken == s.Target.ConfirmationToken {
		t.Fatal(next)
	}
	if !errors.Is(m.Confirm(s.Target.ConfirmationToken), ErrTarget) {
		t.Fatal("stale token")
	}
	if err = m.Confirm(next.Target.ConfirmationToken); err != nil {
		t.Fatal(err)
	}
	if err = m.SetParameter("context", "8192"); err != nil {
		t.Fatal(err)
	}
	copy := m.Snapshot()
	copy.Parameters["context"] = "1"
	copy.Target.Compatibility.Capabilities[0] = "evil"
	copy.Target.Executable = "evil"
	if m.Snapshot().Parameters["context"] != "8192" || m.Snapshot().Target.Executable != rt.path {
		t.Fatal("mutable snapshot")
	}
	if err = m.ResetParameters(); err != nil {
		t.Fatal(err)
	}
	if m.Snapshot().Confirmed || m.Snapshot().Parameters["context"] != "" || m.Snapshot().Parameters["local-port"] != "11434" {
		t.Fatal(m.Snapshot())
	}
}

func TestLifecycleOwnershipIdempotenceBusyAndCrash(t *testing.T) {
	m, rt, active := fakeFor(t, Ollama)
	confirm(t, m)
	ctx := context.Background()
	rt.occupied = &Identity{PID: 999}
	if !errors.Is(m.Start(ctx), ErrOccupied) || len(rt.starts) != 0 || len(rt.stops) != 0 {
		t.Fatal("occupied port modified")
	}
	rt.occupied = nil
	if err := m.Start(ctx); err != nil {
		t.Fatal(err)
	}
	if err := m.Start(ctx); err != nil || len(rt.starts) != 1 {
		t.Fatal("not idempotent", err)
	}
	if _, err := m.Discover(ctx, ""); !errors.Is(err, ErrOwnership) {
		t.Fatal("rediscovery lost owner")
	}
	if !errors.Is(m.SetParameter("local-port", "23456"), ErrOwnership) {
		t.Fatal("running port changed")
	}
	active.Store(2048)
	for _, op := range []func() error{func() error { return m.Stop(ctx) }, func() error { return m.Restart(ctx) }, func() error { return m.LoadModel(ctx, "model:exact") }} {
		if !errors.Is(op(), ErrBusy) {
			t.Fatal("active request bypass")
		}
	}
	if len(rt.stops) != 0 || m.Snapshot().ActiveRequests != 2048 {
		t.Fatal("busy count capped")
	}
	active.Store(0)
	if err := m.SetParameter("parallel", "4"); err != nil {
		t.Fatal(err)
	}
	rt.alive = false
	if s := m.Refresh(); s.State != Crashed || len(rt.starts) != 1 {
		t.Fatal("auto restart", s)
	}
	if err := m.Restart(ctx); err != nil {
		t.Fatal(err)
	}
	if len(rt.stops) != 1 || len(rt.starts) != 2 || rt.starts[1].Command.Environment["OLLAMA_NUM_PARALLEL"] != "4" {
		t.Fatal("restart lost config/cleanup")
	}
	if err := m.Stop(ctx); err != nil {
		t.Fatal(err)
	}
	if m.Snapshot().State != Stopped || m.Snapshot().Owned != nil {
		t.Fatal(m.Snapshot())
	}
}

func TestFailedStartCleansOnlyRetainedOwnership(t *testing.T) {
	for _, failure := range []string{"spawn", "readiness", "cleanup"} {
		t.Run(failure, func(t *testing.T) {
			m, rt, _ := fakeFor(t, Ollama)
			confirm(t, m)
			if failure == "spawn" {
				rt.startErr = ErrCommand
			} else {
				rt.ready = false
			}
			if failure == "cleanup" {
				rt.errorStop = ErrOwnership
			}
			ctx, cancel := context.WithTimeout(context.Background(), 20*time.Millisecond)
			defer cancel()
			if err := m.Start(ctx); err == nil {
				t.Fatal("failure accepted")
			}
			s := m.Snapshot()
			if s.State != Faulted || len(rt.stops) != 1 || rt.stops[0] != rt.identity {
				t.Fatal(s, rt.stops)
			}
			if (s.Owned != nil) != (failure == "cleanup") {
				t.Fatal("cleanup ownership lost", s)
			}
			if failure == "cleanup" {
				rt.errorStop = nil
				if err := m.Stop(context.Background()); err != nil {
					t.Fatal(err)
				}
			}
		})
	}
}

func TestModelLoadRequiresInstalledAndExactLoadedIdentity(t *testing.T) {
	for _, backend := range []Backend{Ollama, LMStudio} {
		t.Run(string(backend), func(t *testing.T) {
			m, rt, _ := fakeFor(t, backend)
			confirm(t, m)
			ctx := context.Background()
			if !errors.Is(m.LoadModel(ctx, "model:exact"), ErrOwnership) {
				t.Fatal("external model modified")
			}
			if err := m.Start(ctx); err != nil {
				t.Fatal(err)
			}
			if !errors.Is(m.LoadModel(ctx, "not-installed"), ErrModel) {
				t.Fatal("unknown model selected")
			}
			rt.loaded = "model:exact-other"
			if !errors.Is(m.LoadModel(ctx, "model:exact"), ErrModel) {
				t.Fatal("substring accepted")
			}
			rt.loaded = "model:exact"
			if err := m.LoadModel(ctx, "model:exact"); err != nil {
				t.Fatal(err)
			}
			if m.Snapshot().Model != "model:exact" {
				t.Fatal(m.Snapshot())
			}
			if backend == Ollama && !strings.HasSuffix(rt.httpPaths[len(rt.httpPaths)-1], "api/ps") {
				t.Fatal("installed is not loaded")
			}
		})
	}
}

func TestLlamaSelectionAndSwitchExactConfirmation(t *testing.T) {
	m, rt, _ := fakeFor(t, LlamaCpp)
	confirm(t, m)
	ctx := context.Background()
	if !errors.Is(m.Start(ctx), ErrModel) {
		t.Fatal("no model")
	}
	model := filepath.Join(t.TempDir(), "модель.gguf")
	if err := m.LoadModel(ctx, model); err != nil {
		t.Fatal(err)
	}
	if m.Snapshot().State != Stopped || len(rt.starts) != 0 {
		t.Fatal("selection claimed running")
	}
	rt.loaded = "модель"
	if err := m.Start(ctx); err != nil {
		t.Fatal(err)
	}
	rt.loaded = "wrong"
	if !errors.Is(m.LoadModel(ctx, filepath.Join(t.TempDir(), "new.gguf")), ErrModel) {
		t.Fatal("wrong model accepted")
	}
	if m.Snapshot().State != Faulted || len(rt.stops) != 2 || rt.alive {
		t.Fatal("failed switch not cleaned")
	}
}

func TestConcurrentMutationsSerialize(t *testing.T) {
	m, rt, _ := fakeFor(t, Ollama)
	confirm(t, m)
	var wg sync.WaitGroup
	for i := 0; i < 40; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if err := m.Start(context.Background()); err != nil {
				t.Error(err)
			}
			_ = m.Snapshot()
		}()
	}
	wg.Wait()
	if len(rt.starts) != 1 {
		t.Fatal("duplicate concurrent start")
	}
	for i := 0; i < 40; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if err := m.SetParameter("context", "8192"); err != nil {
				t.Error(err)
			}
		}()
	}
	wg.Wait()
}

func TestRefreshDoesNotBlockBehindLongLifecycleOperation(t *testing.T) {
	m, _, _ := fakeFor(t, Ollama)
	confirm(t, m)
	m.op.Lock()
	defer m.op.Unlock()
	done := make(chan Snapshot, 1)
	go func() { done <- m.Refresh() }()
	select {
	case snapshot := <-done:
		if !snapshot.Confirmed {
			t.Fatal("lost current state")
		}
	case <-time.After(time.Second):
		t.Fatal("UI refresh blocked behind operation")
	}
}

func TestInvalidDiscoveryAndUnknownRuntimeFailClosed(t *testing.T) {
	for _, version := range []string{"", strings.Repeat("x", 513), "bad\x00version", "0.33.20", "0.33.2.1", "unknown 99.0"} {
		t.Run(version[:min(len(version), 20)], func(t *testing.T) {
			m, rt, _ := fakeFor(t, Ollama)
			rt.version = version
			s, err := m.Discover(context.Background(), "")
			if err == nil {
				if s.Target.Compatibility.Status != "observation-only" || m.Confirm(s.Target.ConfirmationToken) == nil {
					t.Fatal("unknown version trusted")
				}
			}
		})
	}
	if _, err := NewManager(Ollama, nil, func() int { return 0 }); err == nil {
		t.Fatal("nil runtime")
	}
	if _, err := NewManager("Generic", &fakeRuntime{}, func() int { return 0 }); !errors.Is(err, ErrUnsupported) {
		t.Fatal(err)
	}
}

func TestReplacedEndpointCannotReceiveModelMutation(t *testing.T) {
	m, rt, _ := fakeFor(t, Ollama)
	confirm(t, m)
	ctx := context.Background()
	if err := m.Start(ctx); err != nil {
		t.Fatal(err)
	}
	before := len(rt.httpPaths)
	rt.occupied = &Identity{PID: 900, StartedAt: rt.identity.StartedAt, ImagePath: rt.path}
	if !errors.Is(m.LoadModel(ctx, "model:exact"), ErrOwnership) || len(rt.httpPaths) != before {
		t.Fatal("model command sent to replacement")
	}
}
