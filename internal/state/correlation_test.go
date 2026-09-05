package state

import (
	"fmt"
	"testing"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestContextDeltaRejectsAmbiguityAndCrossClientReuse(t *testing.T) {
	tracker := NewCorrelation()
	observe := func(seq int, turn string, value float64, client domain.Client) domain.Metric {
		return tracker.Observe(&domain.Correlation{SessionID: "session", TurnID: turn, Sequence: seq}, client, domain.Ollama, domain.Measured(value, domain.Tokens, "openai_usage", "v1"))
	}
	if observe(1, "first", 10, domain.Cline).Value != nil {
		t.Fatal("first delta fabricated")
	}
	if m := observe(2, "second", 7, domain.Cline); m.Value == nil || *m.Value != -3 || m.Quality != domain.Calculated {
		t.Fatal("negative context change lost")
	}
	for _, m := range []domain.Metric{observe(2, "dup", 100, domain.Cline), observe(1, "old", 100, domain.Cline), observe(3, "second", 100, domain.Cline), observe(4, "gap", 100, domain.Cline), observe(5, "other-client", 200, domain.Hermes)} {
		if m.Value != nil {
			t.Fatal("ambiguous adjacency")
		}
	}
	if m := observe(5, "next", 110, domain.Cline); m.Value == nil || *m.Value != 10 {
		t.Fatal("gap prevents future adjacent pair")
	}
}

func TestContextStateIsBoundedAndRequiresExactUsage(t *testing.T) {
	tracker := NewCorrelation()
	c := &domain.Correlation{SessionID: "s", TurnID: "t", Sequence: 1}
	if tracker.Observe(c, domain.Cline, domain.Ollama, domain.Derived(10, domain.Tokens, domain.Estimated, "v1", "estimate")).Value != nil {
		t.Fatal("estimated context credited")
	}
	for i := 0; i < MaxTrackedSessions+2; i++ {
		c.SessionID = fmt.Sprint(i)
		tracker.Observe(c, domain.Cline, domain.Ollama, domain.Measured(10, domain.Tokens, "backend", "v1"))
	}
	if len(tracker.sessions) != MaxTrackedSessions {
		t.Fatal("unbounded sessions")
	}
}
