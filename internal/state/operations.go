package state

import (
	"crypto/sha256"
	"encoding/hex"
	"strconv"
	"sync"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MaxTrackedOperations = 1024
const MaxOperationRecords = 8192

type operationState struct {
	graph   domain.OperationGraph
	last    int
	turnIDs map[string]bool
	pending []int
	access  uint64
}
type Operations struct {
	mu      sync.Mutex
	items   map[string]*operationState
	access  uint64
	records int
}

func NewOperations() *Operations { return &Operations{items: map[string]*operationState{}} }

func (t *Operations) Observe(o domain.Observation) *domain.OperationGraph {
	c := o.Correlation
	if c == nil || c.OperationID == "" || c.TurnID == "" || c.SessionID == "" || c.Sequence < 1 {
		return nil
	}
	t.mu.Lock()
	defer t.mu.Unlock()
	t.access++
	s := t.items[c.OperationID]
	if s == nil {
		if c.Sequence != 1 || (o.Agent.ToolResults != nil && *o.Agent.ToolResults > 0) {
			return nil
		}
		if len(t.items) >= MaxTrackedOperations {
			t.evict("")
		}
		s = &operationState{graph: domain.OperationGraph{ID: c.OperationID, SessionID: c.SessionID, StartedAt: o.StartedAt, Client: o.Client, Backend: o.Telemetry.Backend, Status: "running", ErrorType: "none", Turns: []domain.TurnRecord{}, Tools: []domain.ToolEvent{}}, turnIDs: map[string]bool{}}
		t.items[c.OperationID] = s
	} else if s.graph.Status != "running" || s.graph.SessionID != c.SessionID || s.graph.Client != o.Client || s.graph.Backend != o.Telemetry.Backend || c.Sequence != s.last+1 || s.turnIDs[c.TurnID] || s.graph.Truncated {
		s.access = t.access
		return nil
	}
	s.access = t.access
	if len(s.pending) > 0 {
		if o.Agent.ToolResults == nil || *o.Agent.ToolResults != len(s.pending) {
			return nil
		}
	} else if o.Agent.ToolResults != nil && *o.Agent.ToolResults > 0 {
		return nil
	}
	additional := 1
	if o.Agent.DetailsComplete {
		additional += len(o.Agent.Tools)
	}
	if len(s.graph.Turns) >= 1024 || len(s.graph.Tools)+additional-1 > 4096 {
		s.graph.Truncated = true
		return snapshotOperation(s)
	}
	for t.records+additional > MaxOperationRecords {
		if !t.evict(c.OperationID) {
			s.graph.Truncated = true
			return snapshotOperation(s)
		}
	}
	for _, index := range s.pending {
		finishTool(&s.graph.Tools[index], o.StartedAt, "completed", "none")
	}
	s.pending = nil
	s.graph.Turns = append(s.graph.Turns, domain.TurnRecord{TurnID: c.TurnID, RequestID: o.RequestID, Sequence: c.Sequence, StartedAt: o.StartedAt, DurationMS: o.DurationMS, Outcome: o.Outcome, ErrorType: o.ErrorType, AvailableTools: o.Agent.AvailableTools.Clone(), InvokedTools: o.Agent.InvokedTools.Clone()})
	t.records++
	s.turnIDs[c.TurnID] = true
	s.last = c.Sequence
	if model := domain.TechnicalIdentifier(o.Telemetry.Model); model != "" {
		s.graph.Model = model
	}
	end := o.StartedAt.Add(time.Duration(o.DurationMS * float64(time.Millisecond)))
	if o.Agent.DetailsComplete {
		for _, tool := range o.Agent.Tools {
			if domain.TechnicalIdentifier(tool.Name) == "" {
				s.graph.Truncated = true
				continue
			}
			id := sha256.Sum256([]byte(c.OperationID + ":" + c.TurnID + ":" + strconv.Itoa(tool.Sequence)))
			s.pending = append(s.pending, len(s.graph.Tools))
			s.graph.Tools = append(s.graph.Tools, domain.ToolEvent{ID: hex.EncodeToString(id[:16]), TurnSequence: c.Sequence, Sequence: tool.Sequence, Name: tool.Name, StartedAt: end, Duration: domain.Missing(domain.Milliseconds, "inspector", "agent-operation-tracker-v1"), Status: "started", ErrorType: "none"})
			t.records++
		}
	}
	if o.Outcome != "completed" || (o.ErrorType != "none" && o.ErrorType != "") {
		s.graph.Status = "error"
		if o.Outcome == "client_cancelled" {
			s.graph.Status = "cancelled"
		}
		s.graph.ErrorType = o.ErrorType
		s.graph.EndedAt = &end
		for _, index := range s.pending {
			finishTool(&s.graph.Tools[index], end, "error", o.ErrorType)
		}
		s.pending = nil
	} else if o.Agent.Completion == "final" && len(s.pending) == 0 && !s.graph.Truncated {
		s.graph.Status = "completed"
		s.graph.EndedAt = &end
	}
	return snapshotOperation(s)
}

func finishTool(tool *domain.ToolEvent, at time.Time, status, errorType string) {
	tool.Status = status
	tool.ErrorType = errorType
	tool.Duration = domain.Derived(max(0, at.Sub(tool.StartedAt).Seconds()*1000), domain.Milliseconds, domain.Calculated, "agent-operation-tracker-v1", "tool-call-to-result-turn-wall-duration-v1")
}
func (t *Operations) evict(except string) bool {
	oldest := ""
	access := ^uint64(0)
	for id, s := range t.items {
		if id != except && s.access < access {
			access = s.access
			oldest = id
		}
	}
	if oldest == "" {
		return false
	}
	s := t.items[oldest]
	t.records -= len(s.graph.Turns) + len(s.graph.Tools)
	delete(t.items, oldest)
	return true
}
func snapshotOperation(s *operationState) *domain.OperationGraph {
	g := s.graph
	if g.EndedAt != nil {
		end := *g.EndedAt
		g.EndedAt = &end
	}
	g.Turns = append([]domain.TurnRecord{}, g.Turns...)
	g.Tools = append([]domain.ToolEvent{}, g.Tools...)
	for i := range g.Turns {
		g.Turns[i].AvailableTools = g.Turns[i].AvailableTools.Clone()
		g.Turns[i].InvokedTools = g.Turns[i].InvokedTools.Clone()
	}
	for i := range g.Tools {
		g.Tools[i].Duration = g.Tools[i].Duration.Clone()
	}
	return &g
}
