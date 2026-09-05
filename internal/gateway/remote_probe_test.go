package gateway

import (
	"context"
	"errors"
	"github.com/Just9120/llm-inspector/internal/domain"
	"net"
	"testing"
	"time"
)

type dialFunc func(context.Context, string, string) (net.Conn, error)

func (f dialFunc) DialContext(c context.Context, n, a string) (net.Conn, error) { return f(c, n, a) }
func remoteConfig() Config {
	return Config{Backend: domain.Ollama, BackendURL: "https://node.tailnet.ts.net/", Port: 5117, Remote: true}
}
func TestRemoteProbeUsesExplicitPrivateTargetAndSeparateCalculatedLatency(t *testing.T) {
	m, err := newRemoteBackendMonitor(remoteConfig(), dialFunc(func(ctx context.Context, network, address string) (net.Conn, error) {
		if network != "tcp" || address != "node.tailnet.ts.net:443" {
			t.Error("wrong destination")
		}
		a, b := net.Pipe()
		b.Close()
		return a, nil
	}))
	if err != nil {
		t.Fatal(err)
	}
	if s := m.Snapshot(); s.Availability != "unknown" || s.NetworkConnectLatency.Value != nil {
		t.Fatal("fabricated initial latency")
	}
	s, err := m.Probe(t.Context())
	if err != nil || s.Availability != "available" || s.NetworkConnectLatency.Quality != domain.Calculated || s.NetworkConnectLatency.SourceVersion != RemoteProbeSource || s.CheckedAt == nil {
		t.Fatal(s, err)
	}
	*s.NetworkConnectLatency.Value = 999
	if *m.Snapshot().NetworkConnectLatency.Value == 999 {
		t.Fatal("snapshot alias")
	}
	for _, url := range []string{"http://node.tailnet.ts.net/", "https://public.example/", "https://node.tailnet.ts.net/path", "https://127.0.0.1/", "https://user:secret@node.tailnet.ts.net/"} {
		c := remoteConfig()
		c.BackendURL = url
		if _, err := NewRemoteBackendMonitor(c); err == nil {
			t.Fatal("unsafe target")
		}
	}
	if _, err := NewRemoteBackendMonitor(DefaultConfig(domain.Ollama)); err == nil {
		t.Fatal("local remote probe")
	}
}
func TestRemoteProbeFailuresAndCancellationNeverLeaveStaleLatency(t *testing.T) {
	for _, dial := range []dialFunc{func(context.Context, string, string) (net.Conn, error) { return nil, errors.New("PRIVATE_HOST_DETAIL") }, func(context.Context, string, string) (net.Conn, error) { panic("probe failure") }, func(context.Context, string, string) (net.Conn, error) { return nil, nil }} {
		m, _ := newRemoteBackendMonitor(remoteConfig(), dial)
		s, err := m.Probe(t.Context())
		if err != nil || s.Availability != "unavailable" || s.NetworkConnectLatency.Value != nil {
			t.Fatal("failure masked")
		}
	}
	entered := make(chan struct{})
	m, _ := newRemoteBackendMonitor(remoteConfig(), dialFunc(func(ctx context.Context, _, _ string) (net.Conn, error) {
		close(entered)
		<-ctx.Done()
		return nil, ctx.Err()
	}))
	ctx, cancel := context.WithCancel(t.Context())
	done := make(chan struct{})
	go func() {
		defer close(done)
		if _, err := m.Probe(ctx); !errors.Is(err, context.Canceled) {
			t.Error("caller cancellation lost")
		}
	}()
	<-entered
	if m.Snapshot().Availability != "probing" {
		t.Fatal("probe state")
	}
	waiting, stop := context.WithTimeout(t.Context(), time.Millisecond)
	defer stop()
	if _, err := m.Probe(waiting); !errors.Is(err, context.DeadlineExceeded) {
		t.Fatal("waiting probe ignores cancellation")
	}
	cancel()
	select {
	case <-done:
	case <-time.After(time.Second):
		t.Fatal("probe hangs")
	}
	if s := m.Snapshot(); s.Availability != "unknown" || s.NetworkConnectLatency.Value != nil {
		t.Fatal("stale probing state")
	}
}
