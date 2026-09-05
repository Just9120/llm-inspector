package resources

import (
	"context"
	"net"
	"net/url"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MaximumSamplesPerRequest = 2048
const MaximumActiveCollections = 128

type Monitor struct {
	probe     Probe
	resolver  Resolver
	sink      func([]domain.ResourceSample)
	interval  atomic.Int64
	active    atomic.Int64
	saturated atomic.Uint64
	failures  atomic.Uint64
	mu        sync.Mutex
	latest    []domain.ResourceSample
	sessions  map[*session]bool
	closed    bool
}
type Health struct {
	Active    int64  `json:"active"`
	Saturated uint64 `json:"saturated"`
	Failures  uint64 `json:"failures"`
}

func NewMonitor(probe Probe, resolver Resolver, sink func([]domain.ResourceSample)) *Monitor {
	m := &Monitor{probe: probe, resolver: resolver, sink: sink, latest: []domain.ResourceSample{}, sessions: map[*session]bool{}}
	m.interval.Store(int64(time.Second))
	return m
}
func (m *Monitor) SetInterval(interval time.Duration) bool {
	if interval < 250*time.Millisecond || interval > 10*time.Second {
		return false
	}
	m.interval.Store(int64(interval))
	return true
}
func (m *Monitor) Health() Health {
	return Health{Active: m.active.Load(), Saturated: m.saturated.Load(), Failures: m.failures.Load()}
}
func (m *Monitor) Latest() []domain.ResourceSample {
	m.mu.Lock()
	defer m.mu.Unlock()
	return cloneSamples(m.latest)
}
func (m *Monitor) publish(samples []domain.ResourceSample) {
	m.mu.Lock()
	defer m.mu.Unlock()
	m.latest = cloneSamples(samples)
}

// Start allocates only bounded bookkeeping; resolution and native probes never
// run on the HTTP forwarding goroutine.
func (m *Monitor) Start(c domain.RequestResourceContext) domain.ResourceSession {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed {
		return &discardSession{}
	}
	if m.active.Load() >= MaximumActiveCollections {
		m.saturated.Add(1)
		return &discardSession{}
	}
	ctx, cancel := context.WithCancel(context.Background())
	s := &session{owner: m, request: c, ctx: ctx, cancel: cancel, done: make(chan struct{}), interval: time.Duration(m.interval.Load()), samples: []domain.ResourceSample{}, stage: domain.StageValue{Stage: domain.QueueWaiting, Evidence: "protocol_observed", SourceVersion: "gateway-resource-stage-v1"}}
	m.sessions[s] = true
	m.active.Add(1)
	go s.run()
	return s
}
func (m *Monitor) Close(ctx context.Context) error {
	m.mu.Lock()
	m.closed = true
	sessions := make([]*session, 0, len(m.sessions))
	for s := range m.sessions {
		sessions = append(sessions, s)
		s.Complete()
	}
	m.mu.Unlock()
	for _, s := range sessions {
		select {
		case <-s.done:
		case <-ctx.Done():
			return ctx.Err()
		}
	}
	return nil
}

type discardSession struct{}

func (*discardSession) StageChanged(domain.StageValue) {}
func (*discardSession) AddSent(int)                    {}
func (*discardSession) AddReceived(int)                {}
func (*discardSession) Complete()                      {}

type session struct {
	owner          *Monitor
	request        RequestContext
	ctx            context.Context
	cancel         context.CancelFunc
	done           chan struct{}
	interval       time.Duration
	sent, received atomic.Uint64
	mu             sync.Mutex
	stage          domain.StageValue
	samples        []domain.ResourceSample
	dropped        int
	association    *domain.ProcessAssociation
	previous       *Snapshot
	local          bool
}

func (s *session) StageChanged(stage domain.StageValue) {
	if !stage.Valid() {
		return
	}
	s.mu.Lock()
	s.stage = stage
	s.mu.Unlock()
}
func (s *session) AddSent(n int) {
	if n > 0 {
		s.sent.Add(uint64(n))
	}
}
func (s *session) AddReceived(n int) {
	if n > 0 {
		s.received.Add(uint64(n))
	}
}
func (s *session) Complete() { s.cancel() }

func (s *session) run() {
	defer close(s.done)
	defer func() { s.owner.mu.Lock(); delete(s.owner.sessions, s); s.owner.mu.Unlock(); s.owner.active.Add(-1) }()
	s.local = isLocal(s.request.BackendURL)
	if s.local && s.owner.resolver != nil {
		func() {
			defer func() {
				if recover() != nil {
					s.owner.failures.Add(1)
				}
			}()
			s.association = s.owner.resolver.Resolve(s.request.BackendURL)
		}()
	}
	s.capture()
	timer := time.NewTicker(s.interval)
	defer timer.Stop()
	for {
		select {
		case <-s.ctx.Done():
			s.finish()
			return
		case <-timer.C:
			s.capture()
		}
	}
}
func (s *session) capture() {
	var current *Snapshot
	if s.local && s.owner.probe != nil {
		func() {
			defer func() {
				if recover() != nil {
					s.owner.failures.Add(1)
				}
			}()
			ctx, cancel := context.WithTimeout(s.ctx, time.Second)
			defer cancel()
			value, err := s.owner.probe.Capture(ctx, s.association)
			if err != nil {
				if s.ctx.Err() == nil {
					s.owner.failures.Add(1)
				}
				return
			}
			current = &value
		}()
	}
	if s.ctx.Err() != nil {
		return
	}
	s.mu.Lock()
	stage := s.stage
	s.mu.Unlock()
	at := time.Now()
	if current != nil {
		at = current.CapturedAt
	}
	samples := Project(s.request, stage, s.association, s.previous, current, Traffic{Sent: s.sent.Load(), Received: s.received.Load()}, s.local, runtime.NumCPU(), at)
	if len(s.samples)+len(samples) <= MaximumSamplesPerRequest {
		s.samples = append(s.samples, samples...)
	} else {
		s.dropped += len(samples)
	}
	for i := range samples {
		samples[i].DroppedSamples = s.dropped
	}
	s.owner.publish(samples)
	// A failed capture clears the baseline; do not label an arbitrarily long
	// delta across an outage as one sampling interval.
	s.previous = current
}
func (s *session) finish() {
	s.mu.Lock()
	stage := s.stage
	s.mu.Unlock()
	// Terminal network counters are current; CPU/GPU values are not copied from
	// an earlier timestamp and presented as fresh terminal measurements.
	terminal := Project(s.request, stage, nil, nil, nil, Traffic{Sent: s.sent.Load(), Received: s.received.Load()}, s.local, runtime.NumCPU(), time.Now())
	if len(s.samples)+len(terminal) > MaximumSamplesPerRequest {
		remove := len(s.samples) + len(terminal) - MaximumSamplesPerRequest
		s.dropped += remove
		s.samples = s.samples[:len(s.samples)-remove]
	}
	for i := range terminal {
		terminal[i].DroppedSamples = s.dropped
	}
	s.samples = append(s.samples, terminal...)
	s.owner.publish(terminal)
	if s.owner.sink != nil {
		func() {
			defer func() {
				if recover() != nil {
					s.owner.failures.Add(1)
				}
			}()
			s.owner.sink(cloneSamples(s.samples))
		}()
	}
}
func isLocal(endpoint string) bool {
	u, err := url.Parse(endpoint)
	if err != nil {
		return false
	}
	host := u.Hostname()
	return strings.EqualFold(host, "localhost") || net.ParseIP(host) != nil && net.ParseIP(host).IsLoopback()
}
func cloneSamples(source []domain.ResourceSample) []domain.ResourceSample {
	result := append([]domain.ResourceSample{}, source...)
	for i := range result {
		r := &result[i]
		if r.Stage != nil {
			v := *r.Stage
			r.Stage = &v
		}
		if r.Process != nil {
			v := *r.Process
			r.Process = &v
		}
		for _, m := range []*domain.Metric{&r.CPU, &r.MemoryPercent, &r.MemoryUsed, &r.ProcessCPU, &r.ProcessMemory, &r.DiskRead, &r.DiskWrite, &r.ClientToBackend, &r.BackendToClient, &r.GPUUtilization, &r.GPUVRAMUsed, &r.GPUVRAMTotal, &r.GPUTemperature, &r.GPUPower} {
			*m = m.Clone()
		}
	}
	return result
}
