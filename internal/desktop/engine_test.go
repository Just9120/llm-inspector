package desktop

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/artifact"
	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/performance"
	"github.com/Just9120/llm-inspector/internal/remote"
	"github.com/Just9120/llm-inspector/internal/resources"
)

type fakeAutostart struct{ enabled bool }

func (a *fakeAutostart) IsEnabled() (bool, error)      { return a.enabled, nil }
func (a *fakeAutostart) SetEnabled(enabled bool) error { a.enabled = enabled; return nil }

type silentPublisher struct{}

func (silentPublisher) Publish(background.Notification) error { return nil }

type unavailableProbe struct{}

func (unavailableProbe) Capture(context.Context, *domain.ProcessAssociation) (resources.Snapshot, error) {
	return resources.Snapshot{}, errors.New("fixture unavailable")
}

type noResolver struct{}

type driverProbe struct {
	ready chan struct{}
	once  sync.Once
}

func (p *driverProbe) Capture(context.Context, *domain.ProcessAssociation) (resources.Snapshot, error) {
	p.once.Do(func() { close(p.ready) })
	used, total := 95.0, 100.0
	return resources.Snapshot{CapturedAt: time.Now(), GPUs: []resources.GPU{{ID: "gpu-0", Driver: "590.41", UsedMiB: &used, TotalMiB: &total}}}, nil
}

func TestRuntimeFactsReachUIHistoryAndSnapshotWithoutInventedVersions(t *testing.T) {
	p := &driverProbe{ready: make(chan struct{})}
	release := make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		select {
		case <-release:
		case <-r.Context().Done():
			return
		}
		w.Header().Set("Content-Type", "application/json")
		fmt.Fprint(w, `{"model":"model-v1","choices":[],"usage":{"prompt_tokens":1}}`)
	}))
	defer server.Close()
	defer func() {
		select {
		case <-release:
		default:
			close(release)
		}
	}()
	d := dependencies(t)
	d.Probe = p
	e := startEngine(t, freeConfig(t, server.URL), d)
	done := make(chan error, 1)
	go func() {
		response, err := http.Post(e.Snapshot().Status.Listener+"/v1/chat/completions", "application/json", strings.NewReader(`{}`))
		if err == nil {
			_, err = io.Copy(io.Discard, response.Body)
			response.Body.Close()
		}
		done <- err
	}()
	select {
	case <-p.ready:
	case <-time.After(5 * time.Second):
		close(release)
		t.Fatal("collector not entered")
	}
	// Wait for the already asynchronous collector to publish, never in relay.
	await(t, func() bool { return len(e.monitor.Latest()) > 0 })
	close(release)
	if err := <-done; err != nil {
		t.Fatal(err)
	}
	await(t, func() bool { return e.Snapshot().Writer.Written >= 2 })
	facts := e.Snapshot().Latest.Runtime
	diagnostic := e.Snapshot()
	pressure := false
	for _, item := range diagnostic.Diagnostics {
		if item.Rule == "vram_pressure" && item.Kind == "fact" {
			pressure = true
		}
	}
	if !pressure || diagnostic.DiagnosticResource == nil || diagnostic.DiagnosticResource.RequestID != diagnostic.Latest.RequestID {
		t.Fatal("completed resource diagnostics disconnected")
	}
	*diagnostic.DiagnosticResource.GPUVRAMUsed.Value = 0
	if *e.Snapshot().DiagnosticResource.GPUVRAMUsed.Value == 0 {
		t.Fatal("mutable diagnostic projection")
	}
	if facts == nil || facts.GPUDriverVersion != "590.41" || facts.ModelVersion != "model-v1" || facts.BackendVersion != "" || facts.ClientVersion != "" {
		t.Fatal(facts)
	}
	if err := e.Gateway.SetRuntimeFacts(*facts); err == nil {
		t.Fatal("changed running facts")
	}
	f := NewFacade(func() *Engine { return e }, Dialogs{})
	preview, err := f.PreviewSnapshot(artifact.TimeRange(time.Now().Add(-time.Minute), time.Now().Add(time.Minute)))
	if err != nil || !strings.Contains(preview.JSON, "590.41") {
		t.Fatal("driver missing in final snapshot", err)
	}
}

func (noResolver) Resolve(string) *domain.ProcessAssociation { return nil }

type unavailableCredentials struct{}

func (unavailableCredentials) Load(context.Context) (remote.Stored, error) {
	return remote.Stored{}, remote.ErrConfiguration
}
func (unavailableCredentials) Save(context.Context, remote.Stored) error {
	return remote.ErrConfiguration
}

func dependencies(t *testing.T) Dependencies {
	t.Helper()
	dir := t.TempDir()
	settings, err := background.NewSettingsStore(filepath.Join(dir, "settings.json"))
	if err != nil {
		t.Fatal(err)
	}
	return Dependencies{DataDirectory: dir, Settings: settings, Autostart: &fakeAutostart{}, Publisher: silentPublisher{}, Credentials: unavailableCredentials{}, Probe: unavailableProbe{}, Resolver: noResolver{}, OSVersion: "10.0.26200"}
}
func freeConfig(t *testing.T, backendURL string) gateway.Config {
	t.Helper()
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := listener.Addr().(*net.TCPAddr).Port
	listener.Close()
	return gateway.Config{Backend: domain.Ollama, BackendURL: backendURL, Port: port}
}
func startEngine(t *testing.T, config gateway.Config, d Dependencies) *Engine {
	t.Helper()
	e, err := Start(config, d)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		if err := e.Close(ctx); err != nil {
			t.Error(err)
		}
	})
	return e
}
func await(t *testing.T, condition func() bool) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if condition() {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatal("condition not reached")
}

func TestEngineComposesRelayHistoryResourcesAndPrivacy(t *testing.T) {
	const private = "desktop-canary-private-prompt-output"
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		if !strings.Contains(string(body), private) {
			t.Error("relay body changed")
		}
		w.Header().Set("Content-Type", "application/json")
		fmt.Fprintf(w, `{"model":"safe-model","choices":[{"message":{"content":"%s"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":5,"total_tokens":8}}`, private)
	}))
	defer server.Close()
	d := dependencies(t)
	e := startEngine(t, freeConfig(t, server.URL), d)
	status := e.Snapshot()
	if !status.Status.ProxyRunning || !status.Status.HistoryAvailable || status.RemoteAccess.Available {
		t.Fatal(status.Status, status.RemoteAccess)
	}
	response, err := http.Post(status.Status.Listener+"/clients/hermes/v1/chat/completions", "application/json", strings.NewReader(`{"model":"safe-model","messages":[{"role":"user","content":"`+private+`"}]}`))
	if err != nil {
		t.Fatal(err)
	}
	body, _ := io.ReadAll(response.Body)
	response.Body.Close()
	if !strings.Contains(string(body), private) {
		t.Fatal("relay output changed")
	}
	await(t, func() bool { return e.Snapshot().Hub.Observations == 1 && e.Snapshot().Writer.Written >= 2 })
	state := e.Snapshot()
	if state.Latest == nil || state.Latest.Client != domain.Hermes || state.Latest.Runtime == nil || state.Latest.Runtime.FrameworkVersion == "" || state.ActiveCount != 0 {
		t.Fatal(state.Latest)
	}
	encoded, _ := json.Marshal(state)
	if strings.Contains(string(encoded), private) {
		t.Fatal("content in UI DTO")
	}
	from, to := time.Now().Add(-time.Minute), time.Now().Add(time.Minute)
	slice, err := e.History.Slice(t.Context(), history.Filter{From: &from, To: &to})
	if err != nil {
		t.Fatal(err)
	}
	if len(slice.Requests) != 1 || len(slice.Resources) == 0 || slice.Resources[0].RequestID != state.Latest.RequestID {
		t.Fatal("resource FK race", len(slice.Requests), len(slice.Resources))
	}
	if err = e.Close(t.Context()); err != nil {
		t.Fatal(err)
	}
	data, err := os.ReadFile(filepath.Join(d.DataDirectory, "data", "inspector.db"))
	if err != nil || strings.Contains(string(data), private) {
		t.Fatal("private content in database", err)
	}
}

func TestEngineOptionalFailuresNeverReplaceDataOrStopProxy(t *testing.T) {
	d := dependencies(t)
	path := filepath.Join(d.DataDirectory, "settings.json")
	original := []byte(`{"schema_version":999,"private":"preserve"}`)
	if err := os.WriteFile(path, original, 0600); err != nil {
		t.Fatal(err)
	}
	if err := os.MkdirAll(filepath.Join(d.DataDirectory, "data"), 0700); err != nil {
		t.Fatal(err)
	}
	dbPath := filepath.Join(d.DataDirectory, "data", "inspector.db")
	invalidDB := []byte("not a database; preserve bytes")
	if err := os.WriteFile(dbPath, invalidDB, 0600); err != nil {
		t.Fatal(err)
	}
	e := startEngine(t, freeConfig(t, "http://127.0.0.1:11434/"), d)
	state := e.Snapshot()
	if !state.Status.ProxyRunning || state.Status.HistoryAvailable || state.Status.SettingsMessage == "" || state.Settings.Monitoring.Profile != performance.Balanced || state.RemoteAccess.Enabled {
		t.Fatal(state.Status)
	}
	if err := e.SaveSettings(background.DefaultSettings()); err == nil {
		t.Fatal("corrupt settings overwritten")
	}
	for name, want := range map[string]string{path: string(original), dbPath: string(invalidDB)} {
		got, err := os.ReadFile(name)
		if err != nil || string(got) != want {
			t.Fatal("unrelated/corrupt data changed", name, err)
		}
	}
}

func TestEngineSettingsAndBusyCountAreActual(t *testing.T) {
	entered, release := make(chan struct{}), make(chan struct{})
	server := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		close(entered)
		<-release
		fmt.Fprint(w, `{"choices":[]}`)
	}))
	defer server.Close()
	d := dependencies(t)
	e := startEngine(t, freeConfig(t, server.URL), d)
	settings := e.Settings.Current()
	settings.Monitoring.Profile = performance.Saver
	if err := e.SaveSettings(settings); err != nil {
		t.Fatal(err)
	}
	if e.Snapshot().Settings.Monitoring.Profile != performance.Saver || !e.ToggleNotifications() || !e.Snapshot().NotificationsPaused {
		t.Fatal("settings/notifications disconnected")
	}
	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		defer wg.Done()
		response, err := http.Post(e.Snapshot().Status.Listener+"/v1/chat/completions", "application/json", strings.NewReader(`{}`))
		if err == nil {
			io.Copy(io.Discard, response.Body)
			response.Body.Close()
		} else {
			t.Error(err)
		}
	}()
	<-entered
	if e.Snapshot().ActiveCount != 1 {
		t.Error("active count not wired")
	}
	close(release)
	wg.Wait()
	await(t, func() bool { return e.Snapshot().ActiveCount == 0 })
}

func TestEnginePortConflictIsVisibleAndNonDestructive(t *testing.T) {
	occupied, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer occupied.Close()
	config := gateway.DefaultConfig(domain.Ollama)
	config.Port = occupied.Addr().(*net.TCPAddr).Port
	e := startEngine(t, config, dependencies(t))
	if e.Snapshot().Status.ProxyRunning || e.Snapshot().Status.Message == "" {
		t.Fatal("port failure hidden")
	}
	if _, err = Start(gateway.Config{}, dependencies(t)); err == nil {
		t.Fatal("invalid launch configuration")
	}
}

func TestHubResourceOrderingAndDetachedProjection(t *testing.T) {
	store, err := history.Open(t.Context(), filepath.Join(t.TempDir(), "history.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	buffer := history.NewBuffered(store)
	defer buffer.Close(t.Context())
	hub := NewHub(buffer, nil, domain.RuntimeFacts{ConfigurationID: "fixture"})
	defer hub.Close(t.Context())
	for i := 1; i <= 30; i++ {
		id := fmt.Sprintf("%032x", i)
		at := time.Now()
		observation := domain.Observation{RequestID: id, StartedAt: at, DurationMS: 1, Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: domain.Generic, Telemetry: domain.MissingTelemetry(domain.Ollama), TTFT: domain.Missing(domain.Milliseconds, "inspector", "test-v1")}
		hub.Observations() <- observation
		sample := domain.MissingResource()
		sample.ID = fmt.Sprintf("%032x", i+100)
		sample.RequestID = id
		sample.CapturedAt = at
		// Allow bounded backpressure in this synthetic test's caller, not relay.
		if !hub.OfferResources([]domain.ResourceSample{sample}) {
			t.Fatal("unexpected timeline drop")
		}
		await(t, func() bool { return buffer.Health().Written >= uint64(i*2) })
	}
	latest, _ := hub.Latest()
	latest.Telemetry.Model = "mutated"
	latest.Runtime.ConfigurationID = "changed"
	next, _ := hub.Latest()
	if next.Telemetry.Model != "" || next.Runtime.ConfigurationID != "fixture" {
		t.Fatal("shared mutable UI state")
	}
	if hub.OfferResources(make([]domain.ResourceSample, resources.MaximumSamplesPerRequest+1)) {
		t.Fatal("unbounded timeline")
	}
	if err := hub.Close(t.Context()); err != nil {
		t.Fatal(err)
	}
	if hub.OfferResources([]domain.ResourceSample{{}}) {
		t.Fatal("accepted after close")
	}
	if err := buffer.Close(t.Context()); err != nil {
		t.Fatal(err)
	}
	if buffer.Health().Failed != 0 || buffer.Health().Written != 60 {
		t.Fatal(buffer.Health())
	}
	from, to := time.Now().Add(-time.Minute), time.Now().Add(time.Minute)
	slice, err := store.Slice(t.Context(), history.Filter{From: &from, To: &to})
	if err != nil || len(slice.Resources) != 30 {
		t.Fatal(err, len(slice.Resources))
	}
	for _, sample := range slice.Resources {
		if sample.RequestID == "" {
			t.Fatal("lost request/resource ordering")
		}
	}
}
