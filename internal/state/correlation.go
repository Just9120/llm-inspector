package state

import (
	"sync"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MaxTrackedSessions = 1024

type sessionKey struct {
	id      string
	client  domain.Client
	backend domain.Backend
}
type sessionState struct {
	turn     string
	sequence int
	context  float64
	access   uint64
}
type Correlation struct {
	mu       sync.Mutex
	sessions map[sessionKey]sessionState
	access   uint64
}

func NewCorrelation() *Correlation { return &Correlation{sessions: map[sessionKey]sessionState{}} }

func (t *Correlation) Observe(c *domain.Correlation, client domain.Client, backend domain.Backend, usage domain.Metric) domain.Metric {
	missing := domain.Missing(domain.TokenDelta, "inspector", "inspector-correlation-headers-v1")
	if c == nil || c.Sequence < 1 || c.SessionID == "" || c.TurnID == "" || usage.Validate() != nil || usage.Unit != domain.Tokens || usage.Quality != domain.Exact {
		return missing
	}
	t.mu.Lock()
	defer t.mu.Unlock()
	t.access++
	key := sessionKey{c.SessionID, client, backend}
	prev, exists := t.sessions[key]
	if exists && (c.Sequence <= prev.sequence || c.TurnID == prev.turn) {
		prev.access = t.access
		t.sessions[key] = prev
		return missing
	}
	if !exists && len(t.sessions) >= MaxTrackedSessions {
		var oldest sessionKey
		access := ^uint64(0)
		for k, v := range t.sessions {
			if v.access < access {
				oldest = k
				access = v.access
			}
		}
		delete(t.sessions, oldest)
	}
	t.sessions[key] = sessionState{c.TurnID, c.Sequence, *usage.Value, t.access}
	if !exists || c.Sequence != prev.sequence+1 {
		return missing
	}
	return domain.Derived(*usage.Value-prev.context, domain.TokenDelta, domain.Calculated, "inspector-correlation-headers-v1", "adjacent-context-delta-v1")
}
