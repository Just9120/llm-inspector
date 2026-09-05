package state

import (
	"fmt"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func agentObservation(seq int, results int, names []string, completion string) domain.Observation {
	o := domain.Observation{RequestID: fmt.Sprint("request-", seq), StartedAt: time.Unix(0, 0).Add(time.Duration(seq-1) * 2 * time.Second), DurationMS: 100, Outcome: "completed", ErrorType: "none", Client: domain.Cline, Telemetry: domain.MissingTelemetry(domain.Ollama), Correlation: &domain.Correlation{SessionID: "session", TurnID: fmt.Sprint("turn-", seq), Sequence: seq, OperationID: "operation"}, Agent: domain.MissingAgentTurn()}
	o.Agent.AvailableTools = domain.Measured(3, domain.Count, "inspector", "agent-v1")
	o.Agent.InvokedTools = domain.Measured(float64(len(names)), domain.Count, "inspector", "agent-v1")
	o.Agent.ToolResults = &results
	o.Agent.DetailsComplete = true
	o.Agent.Completion = completion
	for i, name := range names {
		o.Agent.Tools = append(o.Agent.Tools, domain.ToolCall{Sequence: i, Name: name})
	}
	return o
}

func TestAdjacentTurnsAndToolLifecycle(t *testing.T) {
	tracker := NewOperations()
	first := tracker.Observe(agentObservation(1, 0, []string{"read_file", "list_files"}, "tool_calls"))
	if first == nil || first.Status != "running" || first.Tools[0].Duration.Value != nil {
		t.Fatal("pending tool duration fabricated")
	}
	second := tracker.Observe(agentObservation(2, 2, nil, "final"))
	if second == nil || second.Status != "completed" || len(second.Turns) != 2 || len(second.Tools) != 2 {
		t.Fatal("operation membership")
	}
	for _, tool := range second.Tools {
		if tool.Status != "completed" || tool.Duration.Quality != domain.Calculated || *tool.Duration.Value != 1900 {
			t.Fatal("tool wall-time derivation")
		}
	}
	if first.Tools[0].Status != "started" {
		t.Fatal("snapshot mutated by next turn")
	}
}

func TestOperationRejectsGapsDuplicatesAndForeignIdentity(t *testing.T) {
	tracker := NewOperations()
	tracker.Observe(agentObservation(1, 0, []string{"read_file"}, "tool_calls"))
	for _, mutate := range []func(*domain.Observation){
		func(o *domain.Observation) { o.Correlation.Sequence = 3 }, func(o *domain.Observation) { o.Correlation.TurnID = "turn-1" }, func(o *domain.Observation) { o.Client = domain.Hermes }, func(o *domain.Observation) { o.Telemetry.Backend = domain.LMStudio }, func(o *domain.Observation) { o.Correlation.SessionID = "foreign" }, func(o *domain.Observation) { n := 0; o.Agent.ToolResults = &n },
	} {
		o := agentObservation(2, 1, nil, "final")
		mutate(&o)
		if tracker.Observe(o) != nil {
			t.Fatal("ambiguous membership accepted")
		}
	}
	if got := tracker.Observe(agentObservation(2, 1, nil, "final")); got == nil || got.Status != "completed" {
		t.Fatal("invalid observation contaminated state")
	}
	if tracker.Observe(agentObservation(3, 0, nil, "final")) != nil {
		t.Fatal("terminal operation reopened")
	}
}

func TestOperationBoundsAndCancellation(t *testing.T) {
	tracker := NewOperations()
	o := agentObservation(1, 0, []string{"tool"}, "tool_calls")
	o.Outcome = "client_cancelled"
	o.ErrorType = "client_cancellation"
	got := tracker.Observe(o)
	if got.Status != "cancelled" || got.Tools[0].Status != "error" {
		t.Fatal("cancellation missing")
	}
	for i := 0; i < MaxTrackedOperations+10; i++ {
		o := agentObservation(1, 0, nil, "final")
		o.Correlation.OperationID = fmt.Sprint("operation-", i)
		tracker.Observe(o)
	}
	if len(tracker.items) != MaxTrackedOperations || tracker.records > MaxOperationRecords {
		t.Fatal("unbounded operation state")
	}
}
