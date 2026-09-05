package artifact

import (
	"encoding/json"
	"errors"
	"fmt"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
)

func TestResourceTruncationAndExportRefusal(t *testing.T) {
	s, at := setup(t)
	for start := 0; start < 5001; start += 500 {
		batch := []domain.ResourceSample{}
		for i := start; i < start+500 && i < 5001; i++ {
			r := domain.MissingResource()
			r.ID = fmt.Sprintf("%032x", i+10000)
			r.CapturedAt = at
			batch = append(batch, r)
		}
		if err := s.RecordResources(t.Context(), batch); err != nil {
			t.Fatal(err)
		}
	}
	a, err := CreateSnapshot(t.Context(), s, TimeRange(at, at), EnvironmentFromVersions("", "", "", ""), at)
	if err != nil {
		t.Fatal(err)
	}
	var d Snapshot
	if err = json.Unmarshal([]byte(a.JSON), &d); err != nil {
		t.Fatal(err)
	}
	if len(d.Resources) != 5000 || !d.Truncation.Resources || len(d.Requests) != 0 {
		t.Fatal("resource snapshot bound missing")
	}
	if _, err = CreateExport(t.Context(), s, at, at, at); !errors.Is(err, history.ErrTooLarge) {
		t.Fatal("partial export allowed", err)
	}
}

func TestOperationSnapshotAndNestedAllowlist(t *testing.T) {
	s, at := setup(t)
	opID := "12341234123412341234123412341234"
	o := domain.Observation{RequestID: "56785678567856785678567856785678", StartedAt: at, DurationMS: 5, Client: domain.Generic, Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Telemetry: domain.MissingTelemetry(domain.Ollama), TTFT: domain.Missing(domain.Milliseconds, "inspector", "test-v1")}
	missingCount := domain.Missing(domain.Count, "inspector", "agent-v1")
	g := domain.OperationGraph{ID: opID, StartedAt: at, Client: domain.Generic, Backend: domain.Ollama, Status: "completed", ErrorType: "none", EndedAt: &at, Turns: []domain.TurnRecord{{TurnID: o.RequestID, RequestID: o.RequestID, Sequence: 1, StartedAt: at, DurationMS: 5, Outcome: "completed", ErrorType: "none", AvailableTools: missingCount, InvokedTools: missingCount}}}
	o.Operation = &g
	if err := s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	a, err := CreateSnapshot(t.Context(), s, Operation(opID), EnvironmentFromVersions("10.0.26200", "", "", ""), at)
	if err != nil {
		t.Fatal(err)
	}
	var d Snapshot
	json.Unmarshal([]byte(a.JSON), &d)
	if len(d.Requests) != 1 || d.Requests[0].ID != guid(o.RequestID) || *d.Selection.OperationID != guid(opID) {
		t.Fatal("operation scope lost")
	}
	var root map[string]json.RawMessage
	json.Unmarshal([]byte(a.JSON), &root)
	for key, expected := range map[string][]string{"selection": {"scope", "from_utc", "to_utc", "operation_id"}, "environment": {"operating_system_version", "gpu_driver_version", "backend_version", "client_version", "application_version", "framework_version"}, "truncation": {"requests_truncated", "resource_samples_truncated"}} {
		var m map[string]json.RawMessage
		json.Unmarshal(root[key], &m)
		assertKeys(t, m, expected)
	}
	b, _ := json.Marshal(d.Requests[0].Metrics[0])
	var fields map[string]json.RawMessage
	json.Unmarshal(b, &fields)
	assertKeys(t, fields, []string{"key", "value", "unit", "quality", "source", "source_version", "derivation_version"})
	for _, bad := range []Selection{Operation("bad"), Operation("00000000000000000000000000000000"), TimeRange(at.Add(time.Second), at)} {
		if _, err = CreateSnapshot(t.Context(), s, bad, EnvironmentFromVersions("", "", "", ""), at); err == nil {
			t.Fatal("invalid selection allowed")
		}
	}
}
