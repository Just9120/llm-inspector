package artifact

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
)

func setup(t *testing.T) (*history.Store, time.Time) {
	t.Helper()
	s, err := history.Open(t.Context(), filepath.Join(t.TempDir(), "history.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { s.Close() })
	at := time.Date(2026, 9, 5, 12, 0, 0, 0, time.UTC)
	for i := 1; i <= 3; i++ {
		o := domain.Observation{RequestID: fmt.Sprintf("%032x", i), StartedAt: at.Add(time.Duration(i) * time.Second), DurationMS: float64(i) * 100, Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: domain.OpenCode, Telemetry: domain.MissingTelemetry(domain.LlamaCpp), TTFT: domain.Missing(domain.Milliseconds, "inspector", "test-v1")}
		o.Telemetry.PromptTokens = domain.Measured(float64(i), domain.Tokens, "openai_usage", "test-v1")
		if err = s.Record(t.Context(), o); err != nil {
			t.Fatal(err)
		}
		r := domain.MissingResource()
		r.ID = fmt.Sprintf("%032x", i+10)
		r.RequestID = o.RequestID
		r.CapturedAt = o.StartedAt
		r.CPU = domain.Measured(float64(i)*10, domain.Percent, "windows_api", "test-v1")
		if err = s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
			t.Fatal(err)
		}
	}
	return s, at
}

func TestSnapshotAllowlistSelectionAndExactPreviewSave(t *testing.T) {
	s, at := setup(t)
	from, to := at.Add(time.Second), at.Add(2*time.Second)
	a, err := CreateSnapshot(t.Context(), s, TimeRange(from, to), EnvironmentFromVersions("10.0.26200", "", "", ""), at)
	if err != nil {
		t.Fatal(err)
	}
	var raw map[string]json.RawMessage
	if err = json.Unmarshal([]byte(a.JSON), &raw); err != nil {
		t.Fatal(err)
	}
	assertKeys(t, raw, []string{"schema_version", "generated_at_utc", "selection", "environment", "requests", "resource_samples", "truncation"})
	var d Snapshot
	if err = json.Unmarshal([]byte(a.JSON), &d); err != nil {
		t.Fatal(err)
	}
	if d.Schema != "diagnostic-snapshot-v1" || len(d.Requests) != 2 || len(d.Resources) != 2 || d.Environment.GPUDriver.Value != nil || d.Environment.GPUDriver.Availability != "unavailable" || d.Requests[0].Client != "open_code_desktop" || d.Requests[0].Backend != "llama_cpp" {
		t.Fatalf("bad snapshot %+v", d)
	}
	var items []map[string]json.RawMessage
	json.Unmarshal(raw["requests"], &items)
	assertKeys(t, items[0], []string{"request_id", "operation_id", "started_at_utc", "http_status_code", "outcome", "error_type", "client", "backend", "model", "model_load_disposition", "runtime_metrics"})
	items = nil
	json.Unmarshal(raw["resource_samples"], &items)
	assertKeys(t, items[0], []string{"sample_id", "request_id", "operation_id", "captured_at_utc", "stage", "stage_evidence", "gpu_device_id", "dropped_sample_count", "system_metrics"})
	path := filepath.Join(t.TempDir(), "preview.json")
	if err = Save(t.Context(), a, path); err != nil {
		t.Fatal(err)
	}
	b, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	hash := sha256.Sum256(b)
	if string(b) != a.JSON || hex.EncodeToString(hash[:]) != a.SHA256 {
		t.Fatal("saved bytes differ from preview")
	}
	if err = Save(t.Context(), a, path); err != nil {
		t.Fatal("local overwrite failed", err)
	}
	bad := a
	bad.JSON += " "
	if err = Save(t.Context(), bad, path); !errors.Is(err, ErrArtifact) {
		t.Fatal(err)
	}
	for _, path := range []string{`\\host\share\out.json`, "https://example.invalid/x.json", filepath.Join(t.TempDir(), "out.txt")} {
		if err = Save(t.Context(), a, path); !errors.Is(err, ErrArtifact) {
			t.Fatal(err)
		}
	}
	if _, err = CreateSnapshot(t.Context(), s, Selection{}, EnvironmentFromVersions("", "", "", ""), at); err == nil {
		t.Fatal("unbounded snapshot allowed")
	}
}

func TestAnalyticsExportKeepsV1Contract(t *testing.T) {
	s, at := setup(t)
	a, err := CreateExport(t.Context(), s, at, at.Add(time.Minute), at)
	if err != nil {
		t.Fatal(err)
	}
	var raw map[string]json.RawMessage
	json.Unmarshal([]byte(a.JSON), &raw)
	assertKeys(t, raw, []string{"schema_version", "generated_at_utc", "selection", "history", "aggregate_metrics", "model_loads"})
	var d Export
	if err = json.Unmarshal([]byte(a.JSON), &d); err != nil {
		t.Fatal(err)
	}
	if d.Schema != "analytics-export-v1" || len(d.History.Requests) != 3 || len(d.Aggregates) != 1 || d.ModelLoads.Unavailable != 3 {
		t.Fatal(d)
	}
	found := false
	for _, m := range d.Aggregates[0].Metrics {
		if m.Category == "request" && m.Key == "total_duration_milliseconds" {
			found = true
			if m.SampleCount != 3 || !m.Sufficient || *m.Mean != 200 || *m.P95 != 300 {
				t.Fatal(m)
			}
		}
	}
	if !found {
		t.Fatal("legacy metric name missing")
	}
}

func assertKeys(t *testing.T, m map[string]json.RawMessage, expected []string) {
	t.Helper()
	keys := []string{}
	for k := range m {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	sort.Strings(expected)
	if !reflect.DeepEqual(keys, expected) {
		t.Fatalf("unexpected schema keys %v", keys)
	}
}

func TestProxyContentNeverReachesDatabaseOrArtifacts(t *testing.T) {
	secrets := []string{"PRIVATE_PROMPT_7f98", "PRIVATE_RESPONSE_7f98", "PRIVATE_REASONING_7f98", "PRIVATE_TOOL_ARGS_7f98", "PRIVATE_TOOL_RESULT_7f98", "PRIVATE_USER_CODE_7f98"}
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		io.Copy(io.Discard, r.Body)
		w.Header().Set("Content-Type", "application/json")
		fmt.Fprintf(w, `{"model":"qwen3.5:9b","choices":[{"message":{"content":"%s","reasoning_content":"%s","tool_calls":[{"index":0,"function":{"name":"read_file","arguments":"%s"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":3}}`, secrets[1], secrets[2], secrets[3])
	}))
	defer backend.Close()
	path := filepath.Join(t.TempDir(), "privacy.db")
	s, err := history.Open(t.Context(), path)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	config := gateway.DefaultConfig(domain.Ollama)
	config.BackendURL = backend.URL
	completed := make(chan domain.Observation, 1)
	g, err := gateway.New(config, completed)
	if err != nil {
		t.Fatal(err)
	}
	proxy := httptest.NewServer(g)
	defer proxy.Close()
	body := fmt.Sprintf(`{"model":"qwen3.5:9b","messages":[{"role":"user","content":"%s %s"},{"role":"tool","content":"%s"}],"tools":[{"type":"function","function":{"name":"read_file","parameters":{"description":"%s"}}}]}`, secrets[0], secrets[5], secrets[4], secrets[3])
	resp, err := http.Post(proxy.URL+"/v1/chat/completions", "application/json", strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	io.Copy(io.Discard, resp.Body)
	resp.Body.Close()
	var o domain.Observation
	select {
	case o = <-completed:
	case <-time.After(5 * time.Second):
		t.Fatal("observation missing")
	}
	if err = s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	a, err := CreateSnapshot(t.Context(), s, TimeRange(o.StartedAt.Add(-time.Second), o.StartedAt.Add(time.Minute)), EnvironmentFromVersions("10.0.26200", "", "", ""), time.Now())
	if err != nil {
		t.Fatal(err)
	}
	export, err := CreateExport(t.Context(), s, o.StartedAt.Add(-time.Second), o.StartedAt.Add(time.Minute), time.Now())
	if err != nil {
		t.Fatal(err)
	}
	s.Close()
	for _, p := range []string{path, path + "-wal", path + "-shm"} {
		data, e := os.ReadFile(p)
		if e != nil && !os.IsNotExist(e) {
			t.Fatal(e)
		}
		for _, secret := range secrets {
			if strings.Contains(string(data), secret) {
				t.Fatal("private content persisted")
			}
		}
	}
	for _, secret := range secrets {
		if strings.Contains(a.JSON, secret) || strings.Contains(export.JSON, secret) {
			t.Fatal("private content exported")
		}
	}
}
