// Package state owns bounded in-memory technical projections. It performs no
// filesystem, network, collector or UI calls on the forwarding hot path.
package state

import (
	"sort"
	"sync"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MaxActiveRequests = 1024

type progressSample struct {
	value float64
	at    time.Time
}
type activeRequest struct {
	id       string
	client   domain.Client
	started  time.Time
	stage    domain.StageValue
	progress domain.Metric
	samples  []progressSample
}
type Live struct {
	mu       sync.Mutex
	now      func() time.Time
	active   map[string]*activeRequest
	terminal *domain.LiveRequest
	omitted  uint64
}

func NewLive(now func() time.Time) *Live {
	if now == nil {
		now = time.Now
	}
	return &Live{now: now, active: map[string]*activeRequest{}}
}

func ProtocolStage(stage domain.Stage) domain.StageValue {
	return domain.StageValue{Stage: stage, Evidence: "protocol_observed", SourceVersion: "gateway-lifecycle-v1"}
}
func (l *Live) Start(id string, client domain.Client, started time.Time) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	if id == "" || l.active[id] != nil {
		return false
	}
	if len(l.active) >= MaxActiveRequests {
		l.omitted++
		return false
	}
	l.active[id] = &activeRequest{id: id, client: client, started: started, stage: ProtocolStage(domain.QueueWaiting), progress: domain.Missing(domain.Percent, "backend_extension", "no-backend-progress-v1")}
	return true
}

func (l *Live) Stage(id string, stage domain.StageValue) {
	if !stage.Valid() || stage.Terminal() {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	if r := l.active[id]; r != nil {
		r.stage = stage
	}
}

func (l *Live) Progress(id string, metric domain.Metric) {
	if metric.Validate() != nil || metric.Unit != domain.Percent || metric.Quality != domain.Exact || metric.Source != "backend_extension" {
		return
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	r := l.active[id]
	if r == nil {
		return
	}
	if r.progress.Value == nil || r.progress.SourceVersion != metric.SourceVersion || *metric.Value <= *r.progress.Value {
		r.samples = nil
	}
	r.samples = append(r.samples, progressSample{*metric.Value, l.now()})
	if len(r.samples) > 4 {
		r.samples = r.samples[len(r.samples)-4:]
	}
	r.progress = metric.Clone()
}

func (l *Live) Finish(id, outcome, errorType string) {
	l.mu.Lock()
	defer l.mu.Unlock()
	r := l.active[id]
	if r == nil {
		return
	}
	delete(l.active, id)
	stage := domain.Completed
	if outcome == "client_cancelled" {
		stage = domain.Cancelled
	} else if outcome != "completed" || (errorType != "none" && errorType != "") {
		stage = domain.Failed
	}
	r.stage = ProtocolStage(stage)
	snapshot := l.snapshot(r, l.now())
	snapshot.ETA = domain.Missing(domain.Milliseconds, "inspector", "live-eta-v1")
	l.terminal = &snapshot
}

func (l *Live) Snapshot() domain.LiveSnapshot {
	l.mu.Lock()
	defer l.mu.Unlock()
	now := l.now()
	out := domain.LiveSnapshot{Active: []domain.LiveRequest{}, Omitted: l.omitted}
	for _, r := range l.active {
		out.Active = append(out.Active, l.snapshot(r, now))
	}
	sort.Slice(out.Active, func(i, j int) bool {
		if out.Active[i].StartedAt.Equal(out.Active[j].StartedAt) {
			return out.Active[i].RequestID < out.Active[j].RequestID
		}
		return out.Active[i].StartedAt.Before(out.Active[j].StartedAt)
	})
	if l.terminal != nil {
		r := *l.terminal
		r.Elapsed = r.Elapsed.Clone()
		r.Progress = r.Progress.Clone()
		r.ETA = r.ETA.Clone()
		out.LatestTerminal = &r
	}
	return out
}

func (l *Live) snapshot(r *activeRequest, now time.Time) domain.LiveRequest {
	eta := domain.Missing(domain.Milliseconds, "inspector", "live-eta-v1")
	if len(r.samples) >= 3 {
		first, last := r.samples[0], r.samples[len(r.samples)-1]
		span := last.value - first.value
		observed := last.at.Sub(first.at).Seconds() * 1000
		if span >= 5 && last.value < 100 && observed > 0 {
			eta = domain.Derived(observed*(100-last.value)/span, domain.Milliseconds, domain.Estimated, "live-eta-v1", "linear-backend-progress-v1")
		}
	}
	return domain.LiveRequest{RequestID: r.id, Client: r.client, StartedAt: r.started.UTC(), Stage: r.stage, Elapsed: domain.Derived(max(0, now.Sub(r.started).Seconds()*1000), domain.Milliseconds, domain.Calculated, "monotonic-clock-v1", "monotonic-elapsed-v1"), Progress: r.progress.Clone(), ETA: eta}
}
