package history

import (
	"errors"
	"fmt"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func graph(o domain.Observation) domain.OperationGraph {
	return domain.OperationGraph{ID: fmt.Sprintf("%032x", 100), SessionID: fmt.Sprintf("%032x", 200), StartedAt: o.StartedAt, Client: o.Client, Backend: o.Telemetry.Backend, Model: o.Telemetry.Model, Status: "running", ErrorType: "none", Turns: []domain.TurnRecord{{TurnID: fmt.Sprintf("%032x", 300), RequestID: o.RequestID, Sequence: 1, StartedAt: o.StartedAt, DurationMS: o.DurationMS, Outcome: o.Outcome, ErrorType: "none", AvailableTools: domain.Measured(2, domain.Count, "inspector", "agent-v1"), InvokedTools: domain.Measured(1, domain.Count, "inspector", "agent-v1")}}, Tools: []domain.ToolEvent{{ID: fmt.Sprintf("%032x", 400), TurnSequence: 1, Sequence: 0, Name: "read_file", StartedAt: o.StartedAt, Duration: domain.Missing(domain.Milliseconds, "inspector", "agent-v1"), Status: "started", ErrorType: "none"}}}
}
func resource(n int, at time.Time) domain.ResourceSample {
	r := domain.MissingResource()
	r.ID = fmt.Sprintf("%032x", n+10000)
	r.CapturedAt = at
	r.CPU = domain.Measured(25, domain.Percent, "windows_api", "windows-v1")
	r.Process = &domain.ProcessAssociation{PID: 42, StartedAt: at.Add(-time.Hour), ImageName: "ollama.exe", SourceVersion: "listener-pid-v1"}
	r.Stage = &domain.StageValue{Stage: domain.Generating, Evidence: "protocol_observed", SourceVersion: "gateway-v1"}
	return r
}

func TestOperationResourceRoundTripAndAtomicity(t *testing.T) {
	s := testStore(t)
	o := observation(1)
	g := graph(o)
	o.Operation = &g
	o.Correlation = &domain.Correlation{SessionID: g.SessionID, TurnID: g.Turns[0].TurnID, Sequence: 1, OperationID: g.ID}
	if err := s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	r := resource(1, o.StartedAt)
	r.RequestID = o.RequestID
	r.OperationID = g.ID
	if err := s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
		t.Fatal(err)
	}
	d, err := s.Operation(t.Context(), g.ID)
	if err != nil {
		t.Fatal(err)
	}
	if d == nil || len(d.Graph.Turns) != 1 || len(d.Graph.Tools) != 1 || d.Graph.Tools[0].Duration.Value != nil || len(d.Resources) != 1 || d.Resources[0].Process.PID != 42 || *d.Resources[0].CPU.Value != 25 {
		t.Fatalf("bad detail %+v", d)
	}
	g.Status = "completed"
	end := o.StartedAt.Add(time.Second)
	g.EndedAt = &end
	g.Tools[0].Status = "completed"
	g.Tools[0].Duration = domain.Derived(1000, domain.Milliseconds, domain.Calculated, "agent-v1", "tool-duration-v1")
	if err = s.RecordOperation(t.Context(), g); err != nil {
		t.Fatal(err)
	}
	d, err = s.Operation(t.Context(), g.ID)
	if err != nil || *d.Graph.Tools[0].Duration.Value != 1000 || d.Graph.Status != "completed" || len(d.Graph.Turns) != 1 {
		t.Fatal(d, err)
	}
	slice, err := s.Slice(t.Context(), Filter{OperationID: g.ID})
	if err != nil || len(slice.Requests) != 1 || len(slice.Resources) != 1 {
		t.Fatal(slice, err)
	}
	if slice.Requests[0].OperationID != g.ID {
		t.Fatal("request operation link missing")
	}
	o2 := observation(2)
	g2 := graph(o2)
	g2.Tools[0].Name = "PRIVATE TOOL ARGUMENTS"
	o2.Operation = &g2
	if err = s.Record(t.Context(), o2); !errors.Is(err, ErrInvalid) {
		t.Fatal(err)
	}
	all, _ := s.Query(t.Context(), Filter{})
	if len(all.Items) != 1 {
		t.Fatal("partial invalid request committed")
	}
	if got, err := s.Operation(t.Context(), fmt.Sprintf("%032x", 999)); err != nil || got != nil {
		t.Fatal(got, err)
	}
}

func TestDroppedParentDoesNotDropLaterMetadata(t *testing.T) {
	s := testStore(t)
	o := observation(1)
	g := graph(o)
	if err := s.RecordOperation(t.Context(), g); err != nil {
		t.Fatal(err)
	}
	d, err := s.Operation(t.Context(), g.ID)
	if err != nil {
		t.Fatal(err)
	}
	if d.Graph.Turns[0].RequestID != "" {
		t.Fatal("absent request fabricated")
	}
	r := resource(1, o.StartedAt)
	r.RequestID = o.RequestID
	if err = s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
		t.Fatal(err)
	}
	slice, err := s.Slice(t.Context(), Filter{})
	if err != nil || len(slice.Resources) != 1 || slice.Resources[0].RequestID != "" {
		t.Fatal(slice, err)
	}
}

func TestRetentionBoundaryAndNoNewerCascade(t *testing.T) {
	s := testStore(t)
	now := time.Date(2026, 9, 5, 12, 0, 0, 0, time.UTC)
	cutoff := now.Add(-7 * 24 * time.Hour)
	old := observation(1)
	old.StartedAt = cutoff.Add(-time.Hour)
	g := graph(old)
	old.Operation = &g
	if err := s.Record(t.Context(), old); err != nil {
		t.Fatal(err)
	}
	equal := observation(2)
	equal.StartedAt = cutoff
	if err := s.Record(t.Context(), equal); err != nil {
		t.Fatal(err)
	}
	r := resource(1, cutoff.Add(time.Hour))
	r.RequestID = old.RequestID
	r.OperationID = g.ID
	if err := s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
		t.Fatal(err)
	}
	if _, err := s.ApplyRetention(t.Context(), SevenDays, now); err != nil {
		t.Fatal(err)
	}
	slice, err := s.Slice(t.Context(), Filter{})
	if err != nil {
		t.Fatal(err)
	}
	if len(slice.Resources) != 1 || len(slice.Requests) != 2 {
		t.Fatal("newer child/equal boundary deleted", slice)
	}
	if _, err = s.ApplyRetention(t.Context(), SevenDays, now.Add(2*time.Hour)); err != nil {
		t.Fatal(err)
	}
	slice, err = s.Slice(t.Context(), Filter{})
	if err != nil || len(slice.Resources) != 0 || len(slice.Requests) != 0 {
		t.Fatal(slice, err)
	}
	for _, retention := range []Retention{SevenDays, ThirtyDays, NinetyDays, Indefinite} {
		if err = s.SetRetention(t.Context(), retention); err != nil {
			t.Fatal(err)
		}
		v, e := s.Retention(t.Context())
		if e != nil || v != retention {
			t.Fatal(v, e)
		}
	}
	if _, err = s.ApplyRetention(t.Context(), Indefinite, now); err != nil {
		t.Fatal(err)
	}
}

func TestClearRequiresExactPreviewAndScope(t *testing.T) {
	s := testStore(t)
	if err := s.Record(t.Context(), observation(1)); err != nil {
		t.Fatal(err)
	}
	p, err := s.PreviewClear(t.Context(), ClearScope{All: true})
	if err != nil {
		t.Fatal(err)
	}
	if _, err = s.Clear(t.Context(), p, false); !errors.Is(err, ErrConfirmation) {
		t.Fatal(err)
	}
	if err = s.Record(t.Context(), observation(2)); err != nil {
		t.Fatal(err)
	}
	if _, err = s.Clear(t.Context(), p, true); !errors.Is(err, ErrConfirmation) {
		t.Fatal("stale preview accepted", err)
	}
	o := observation(1)
	p, err = s.PreviewClear(t.Context(), ClearScope{From: &o.StartedAt, To: &o.StartedAt})
	if err != nil {
		t.Fatal(err)
	}
	if p.Counts["requests"] != 1 {
		t.Fatal(p)
	}
	if _, err = s.Clear(t.Context(), p, true); err != nil {
		t.Fatal(err)
	}
	r, err := s.Query(t.Context(), Filter{})
	if err != nil || len(r.Items) != 1 || r.Items[0].RequestID != observation(2).RequestID {
		t.Fatal(r, err)
	}
	if _, err = s.PreviewClear(t.Context(), ClearScope{}); !errors.Is(err, ErrInvalid) {
		t.Fatal(err)
	}
}

func TestCorrelationIdentityCannotBeReassignedAcrossClients(t *testing.T) {
	s := testStore(t)
	o := observation(1)
	g := graph(o)
	o.Operation = &g
	if err := s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	other := g
	other.Client = domain.Hermes
	if err := s.RecordOperation(t.Context(), other); !errors.Is(err, ErrInvalid) {
		t.Fatal("operation identity reused", err)
	}
	other.ID = "abcdefabcdefabcdefabcdefabcdefab"
	if err := s.RecordOperation(t.Context(), other); !errors.Is(err, ErrInvalid) {
		t.Fatal("session identity reused", err)
	}
	other.Client = g.Client
	other.SessionID = ""
	if err := s.RecordOperation(t.Context(), other); !errors.Is(err, ErrInvalid) {
		t.Fatal("turn identity reused", err)
	}
	d, err := s.Operation(t.Context(), g.ID)
	if err != nil || d.Graph.Client != domain.Generic {
		t.Fatal("original graph altered", err)
	}
}
