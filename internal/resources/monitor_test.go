package resources

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type probeFunc func(context.Context, *domain.ProcessAssociation) (Snapshot, error)

func (f probeFunc) Capture(c context.Context, p *domain.ProcessAssociation) (Snapshot, error) {
	return f(c, p)
}

type resolverFunc func(string) *domain.ProcessAssociation

func TestDriverEvidenceIsRequestScopedAndNeverProbesOnRead(t *testing.T) {
	calls := 0
	driver := "590.41"
	m := NewMonitor(probeFunc(func(context.Context, *domain.ProcessAssociation) (Snapshot, error) {
		calls++
		return Snapshot{CapturedAt: time.Now(), GPUs: []GPU{{ID: "gpu-0", Driver: driver}}}, nil
	}), nil, nil)
	s := &session{owner: m, ctx: context.Background(), local: true}
	if s.GPUDriverVersion() != "" || calls != 0 {
		t.Fatal("getter probed or fabricated data")
	}
	s.capture()
	if s.GPUDriverVersion() != driver || calls != 1 {
		t.Fatal("captured driver missing")
	}
	driver = "591.1"
	s.capture()
	if s.GPUDriverVersion() != "" {
		t.Fatal("mixed drivers reported as one version")
	}
	other := &session{owner: m, ctx: context.Background()}
	if other.GPUDriverVersion() != "" {
		t.Fatal("driver leaked across requests")
	}
}

func (f resolverFunc) Resolve(s string) *domain.ProcessAssociation { return f(s) }
func closeMonitor(t *testing.T, m *Monitor) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	if err := m.Close(ctx); err != nil {
		t.Fatal(err)
	}
}
func TestMonitorNeverBlocksRelayAndTerminalCountersAreFresh(t *testing.T) {
	entered := make(chan struct{})
	records := make(chan []domain.ResourceSample, 1)
	m := NewMonitor(probeFunc(func(ctx context.Context, _ *domain.ProcessAssociation) (Snapshot, error) {
		close(entered)
		<-ctx.Done()
		return Snapshot{}, ctx.Err()
	}), nil, func(s []domain.ResourceSample) { records <- s })
	s := m.Start(domain.RequestResourceContext{RequestID: "11111111111111111111111111111111", BackendURL: "http://127.0.0.1:11434"})
	select {
	case <-entered:
	case <-time.After(time.Second):
		t.Fatal("probe not started")
	}
	s.AddSent(17)
	s.AddReceived(31)
	s.StageChanged(domain.StageValue{Stage: domain.Completed, Evidence: "protocol_observed", SourceVersion: "fixture-v1"})
	s.Complete()
	s.Complete()
	closeMonitor(t, m)
	rows := <-records
	if len(rows) != 1 {
		t.Fatalf("terminal rows: %d", len(rows))
	}
	r := rows[0]
	if r.CPU.Value != nil || r.Process != nil || r.Stage.Stage != domain.Completed || *r.ClientToBackend.Value != 17 || *r.BackendToClient.Value != 31 {
		t.Fatal("stale or missing terminal facts")
	}
	latest := m.Latest()
	*latest[0].ClientToBackend.Value = 999
	latest[0].Stage.Stage = domain.Failed
	if *m.Latest()[0].ClientToBackend.Value != 17 || m.Latest()[0].Stage.Stage != domain.Completed {
		t.Fatal("mutable live alias")
	}
	if m.Health().Active != 0 || m.Health().Failures != 0 {
		t.Fatal(m.Health())
	}
}
func TestMonitorBoundedCollectionsAndRemoteIsolation(t *testing.T) {
	var probes, resolves atomic.Int32
	m := NewMonitor(probeFunc(func(context.Context, *domain.ProcessAssociation) (Snapshot, error) {
		probes.Add(1)
		return Snapshot{}, nil
	}), resolverFunc(func(string) *domain.ProcessAssociation { resolves.Add(1); return nil }), nil)
	if m.SetInterval(249*time.Millisecond) || m.SetInterval(10001*time.Millisecond) || !m.SetInterval(10*time.Second) {
		t.Fatal("interval bounds")
	}
	for i := 0; i < MaximumActiveCollections+50; i++ {
		m.Start(domain.RequestResourceContext{BackendURL: "https://node.test.ts.net"})
	}
	if m.Health().Active != MaximumActiveCollections || m.Health().Saturated != 50 {
		t.Fatal(m.Health())
	}
	m.mu.Lock()
	count := len(m.sessions)
	m.mu.Unlock()
	if count != MaximumActiveCollections {
		t.Fatal("unbounded bookkeeping")
	}
	closeMonitor(t, m)
	m.Start(domain.RequestResourceContext{}).Complete()
	if probes.Load() != 0 || resolves.Load() != 0 || m.Health().Active != 0 {
		t.Fatal("remote probes or closed monitor restart")
	}
}
func TestMonitorCollectorAndSinkFailuresAreIsolated(t *testing.T) {
	for _, panicProbe := range []bool{false, true} {
		t.Run(map[bool]string{false: "error", true: "panic"}[panicProbe], func(t *testing.T) {
			entered := make(chan struct{}, 1)
			m := NewMonitor(probeFunc(func(context.Context, *domain.ProcessAssociation) (Snapshot, error) {
				entered <- struct{}{}
				if panicProbe {
					panic("driver failure")
				}
				return Snapshot{}, errors.New("driver failure")
			}), resolverFunc(func(string) *domain.ProcessAssociation { panic("resolver failure") }), func([]domain.ResourceSample) { panic("sink failure") })
			s := m.Start(domain.RequestResourceContext{BackendURL: "http://localhost:11434"})
			select {
			case <-entered:
			case <-time.After(time.Second):
				t.Fatal("probe missing")
			}
			s.Complete()
			closeMonitor(t, m)
			if m.Health().Failures < 2 || m.Health().Active != 0 {
				t.Fatal(m.Health())
			}
		})
	}
}
func TestMonitorSamplingCapAndOutageReset(t *testing.T) {
	var calls int
	m := NewMonitor(probeFunc(func(context.Context, *domain.ProcessAssociation) (Snapshot, error) {
		calls++
		if calls == 2 {
			return Snapshot{}, errors.New("gap")
		}
		return Snapshot{CapturedAt: time.Now(), CPUAvailable: true, Kernel: uint64(calls * 100), Idle: uint64(calls * 20)}, nil
	}), nil, nil)
	s := &session{owner: m, request: RequestContext{}, ctx: context.Background(), local: true, stage: domain.StageValue{Stage: domain.Generating, Evidence: "protocol_observed", SourceVersion: "fixture-v1"}}
	s.capture()
	s.capture()
	s.capture()
	if s.samples[2].CPU.Value != nil {
		t.Fatal("delta spans missing interval")
	}
	for i := 0; i < MaximumSamplesPerRequest+10; i++ {
		s.capture()
	}
	s.AddReceived(456)
	s.finish()
	if len(s.samples) != MaximumSamplesPerRequest || s.dropped < 10 {
		t.Fatal("sample cap missing")
	}
	last := s.samples[len(s.samples)-1]
	if last.DroppedSamples != s.dropped || last.CPU.Value != nil || *last.BackendToClient.Value != 456 {
		t.Fatal("terminal evidence")
	}
}

func TestCollectorZeroTimestampCannotPoisonResourceTimeline(t *testing.T) {
	m := NewMonitor(probeFunc(func(context.Context, *domain.ProcessAssociation) (Snapshot, error) { return Snapshot{}, nil }), nil, nil)
	defer closeMonitor(t, m)
	s := &session{owner: m, request: RequestContext{}, ctx: context.Background(), local: true, stage: domain.StageValue{Stage: domain.Generating, Evidence: "protocol_observed", SourceVersion: "fixture-v1"}}
	s.capture()
	if len(s.samples) != 1 || s.samples[0].CapturedAt.IsZero() || s.samples[0].CPU.Value != nil || m.Health().Failures != 1 {
		t.Fatal("invalid collector data admitted", s.samples, m.Health())
	}
}
