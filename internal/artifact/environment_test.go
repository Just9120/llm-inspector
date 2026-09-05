package artifact

import (
	"encoding/json"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
)

func TestSnapshotUsesOnlySelectedObservedVersionsAndRejectsAmbiguity(t *testing.T) {
	s, at := setup(t)
	o := domain.Observation{RequestID: "22222222222242228222222222222222", StartedAt: at.Add(time.Second), Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: domain.Generic, Telemetry: domain.MissingTelemetry(domain.Ollama), TTFT: domain.Missing(domain.Milliseconds, "inspector", "test-v1"), Runtime: &domain.RuntimeFacts{ConfigurationID: "test", GPUDriverVersion: "590.41"}}
	if err := s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	r := domain.MissingResource()
	r.ID, r.RequestID = "11111111111141118111111111111111", o.RequestID
	r.CapturedAt, r.GPUDriverVersion = at.Add(time.Second), "590.41"
	if err := s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
		t.Fatal(err)
	}
	for _, tc := range []struct {
		from, to time.Time
		driver   string
	}{
		{at, at.Add(2 * time.Second), "590.41"},
		{at.Add(2 * time.Second), at.Add(3 * time.Second), ""},
	} {
		a, err := CreateSnapshot(t.Context(), s, TimeRange(tc.from, tc.to), EnvironmentFromVersions("", "", "", ""), at)
		if err != nil {
			t.Fatal(err)
		}
		var snapshot Snapshot
		if err := json.Unmarshal([]byte(a.JSON), &snapshot); err != nil {
			t.Fatal(err)
		}
		if tc.driver == "" && snapshot.Environment.GPUDriver.Value != nil || tc.driver != "" && (snapshot.Environment.GPUDriver.Value == nil || *snapshot.Environment.GPUDriver.Value != tc.driver) {
			t.Fatal("snapshot used unrelated/missing version", snapshot.Environment.GPUDriver)
		}
	}
	selected := history.Slice{Resources: []domain.ResourceSample{{GPUDriverVersion: "590.41"}, {GPUDriverVersion: "591.1"}}, Requests: []history.Request{{Observation: domain.Observation{Runtime: &domain.RuntimeFacts{ConfigurationID: "test", BackendVersion: "0.33.2", ClientVersion: "1.0"}}}}}
	env := withRecordedVersions(EnvironmentFromVersions("", "", "", ""), selected)
	if env.GPUDriver.Value != nil || env.Backend.Value == nil || *env.Backend.Value != "0.33.2" || env.Client.Value == nil {
		t.Fatal(env)
	}
	selected.Resources = []domain.ResourceSample{{GPUDriverVersion: `C:\private\driver`}}
	if withRecordedVersions(EnvironmentFromVersions("", "", "", ""), selected).GPUDriver.Value != nil {
		t.Fatal("path accepted")
	}
}
