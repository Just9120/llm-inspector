package desktop

import (
	"errors"
	"os"
	"path/filepath"
	"reflect"
	"slices"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/artifact"
	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/lifecycle"
)

func facadeFixture(t *testing.T, dialogs Dialogs) (*Facade, *Engine) {
	t.Helper()
	e := startEngine(t, freeConfig(t, "http://127.0.0.1:11434"), dependencies(t))
	return NewFacade(func() *Engine { return e }, dialogs), e
}

func TestFacadePreviewSavesOnlyExactCachedArtifactViaNativeDialog(t *testing.T) {
	path := filepath.Join(t.TempDir(), "снимок.json")
	dialogCalls := 0
	f, _ := facadeFixture(t, Dialogs{SaveJSON: func(name string) (string, error) {
		dialogCalls++
		if filepath.Ext(name) != ".json" || filepath.Base(name) != name {
			t.Fatal("unsafe default name")
		}
		return path, nil
	}})
	from, to := time.Now().Add(-time.Hour), time.Now()
	preview, err := f.PreviewSnapshot(artifact.TimeRange(from, to))
	if err != nil {
		t.Fatal(err)
	}
	if ok, err := f.SavePreview("modified"); ok || !errors.Is(err, ErrPreview) || dialogCalls != 0 {
		t.Fatal("unconfirmed artifact accepted")
	}
	preview.JSON = "private content supplied by UI"
	if ok, err := f.SavePreview(preview.SHA256); !ok || err != nil {
		t.Fatal(ok, err)
	}
	data, err := os.ReadFile(path)
	if err != nil || string(data) == preview.JSON {
		t.Fatal("caller changed cached bytes", err)
	}
	export, err := f.PreviewExport(from, to)
	if err != nil {
		t.Fatal(err)
	}
	if ok, err := f.SavePreview(preview.SHA256); ok || !errors.Is(err, ErrPreview) {
		t.Fatal("old preview accepted")
	}
	f.dialogs.SaveJSON = func(string) (string, error) { return "", nil }
	if ok, err := f.SavePreview(export.SHA256); ok || err != nil {
		t.Fatal("cancelled dialog saved", err)
	}
	if _, err := f.PreviewSnapshot(artifact.Selection{}); err == nil {
		t.Fatal("invalid selection")
	}
	if ok, err := f.SavePreview(export.SHA256); ok || !errors.Is(err, ErrPreview) {
		t.Fatal("failed preview retained old artifact")
	}
}

func TestFacadeClearConfirmationIsExactOneUseAndDetectsDatabaseDrift(t *testing.T) {
	f, e := facadeFixture(t, Dialogs{})
	preview, err := f.PreviewClear(history.ClearScope{All: true})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := f.ConfirmClear(preview.Token, false); !errors.Is(err, ErrPreview) {
		t.Fatal("confirmation bypass")
	}
	status := 200
	o := domain.Observation{RequestID: "11111111-1111-4111-8111-111111111111", StartedAt: time.Now(), HTTPStatus: &status, Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: domain.Generic, Telemetry: domain.MissingTelemetry(domain.Ollama), TTFT: domain.Missing(domain.Milliseconds, "inspector", "test-v1"), ContextChange: domain.Missing(domain.TokenDelta, "inspector", "test-v1"), Agent: domain.MissingAgentTurn()}
	if err := e.History.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	if _, err := f.ConfirmClear(preview.Token, true); err == nil {
		t.Fatal("drift silently deleted new records")
	}
	rows, err := f.GetHistory(history.Filter{})
	if err != nil || len(rows.Items) != 1 {
		t.Fatal(rows, err)
	}
	preview, err = f.PreviewClear(history.ClearScope{All: true})
	if err != nil || preview.Counts["requests"] != 1 {
		t.Fatal(preview, err)
	}
	if _, err := f.ConfirmClear(preview.Token, true); err != nil {
		t.Fatal(err)
	}
	if _, err := f.ConfirmClear(preview.Token, true); !errors.Is(err, ErrPreview) {
		t.Fatal("confirmation replay")
	}
	rows, err = f.GetHistory(history.Filter{})
	if err != nil || len(rows.Items) != 0 {
		t.Fatal(rows, err)
	}
}

func TestFacadeAvailabilityValidationAndSettings(t *testing.T) {
	notReady := NewFacade(nil, Dialogs{})
	if _, err := notReady.GetState(); !errors.Is(err, ErrNotReady) {
		t.Fatal(err)
	}
	if _, err := notReady.GetHistory(history.Filter{}); !errors.Is(err, ErrNotReady) {
		t.Fatal(err)
	}
	if _, err := notReady.ChooseModel(); !errors.Is(err, ErrNotReady) {
		t.Fatal(err)
	}
	f, e := facadeFixture(t, Dialogs{})
	if _, err := f.GetLifecycle(lifecycle.Ollama); !errors.Is(err, lifecycle.ErrUnsupported) {
		t.Fatal(err)
	}
	if _, err := f.GetLifecycleParameters("unknown"); !errors.Is(err, lifecycle.ErrUnsupported) {
		t.Fatal(err)
	}
	if _, err := f.GetLifecycleParameters(lifecycle.Ollama); err != nil {
		t.Fatal(err)
	}
	if _, err := f.EnableRemote(true); err == nil {
		t.Fatal("unavailable credentials accepted")
	}
	if _, err := f.ProbeRemoteBackend(); err == nil {
		t.Fatal("unconfigured remote probe")
	}
	if _, err := f.GetHistory(history.Filter{Limit: 1001}); err == nil {
		t.Fatal("unbounded query")
	}
	if _, err := f.Compare(history.Filter{}, history.Filter{}, "arbitrary_sql"); err == nil {
		t.Fatal("unbounded metric")
	}
	if _, err := f.Analyze(history.Filter{}); err != nil {
		t.Fatal(err)
	}
	if _, err := f.GetHistoryDetails(history.Filter{}); err != nil {
		t.Fatal(err)
	}
	if _, err := f.GetRetention(); err != nil {
		t.Fatal(err)
	}
	if _, err := f.SetRetention(history.Indefinite); err != nil {
		t.Fatal(err)
	}
	if _, err := f.SetRetention("invalid"); err == nil {
		t.Fatal("invalid retention")
	}
	settings := background.DefaultSettings()
	if err := f.SaveSettings(settings); err != nil {
		t.Fatal(err)
	}
	if paused, err := f.ToggleNotifications(); !paused || err != nil {
		t.Fatal(paused, err)
	}
	if err := e.Close(t.Context()); err != nil {
		t.Fatal(err)
	}
	if _, err := f.GetState(); !errors.Is(err, ErrNotReady) {
		t.Fatal("closed engine exposed")
	}
}

func TestBoundFacadeMethodAllowlist(t *testing.T) {
	// Wails binds all exported methods. An accidental broad helper must fail CI.
	want := []string{"Analyze", "ChooseExecutable", "ChooseModel", "Compare", "ConfirmBackend", "ConfirmClear", "DisableRemote", "DiscoverBackend", "EnableRemote", "Exit", "GetHistory", "GetHistoryDetails", "GetLifecycle", "GetLifecycleParameters", "GetModels", "GetOperation", "GetRetention", "GetState", "HideWindow", "LoadModel", "PreviewClear", "PreviewExport", "PreviewSnapshot", "ProbeRemoteBackend", "ResetBackendParameters", "RestartBackend", "RotateRemoteToken", "SavePreview", "SaveSettings", "SetBackendParameter", "SetBackendParameters", "SetRetention", "StartBackend", "StopBackend", "ToggleNotifications"}
	slices.Sort(want)
	typ := reflect.TypeFor[*Facade]()
	got := []string{}
	for i := 0; i < typ.NumMethod(); i++ {
		got = append(got, typ.Method(i).Name)
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatal("review desktop binding surface", got)
	}
}
