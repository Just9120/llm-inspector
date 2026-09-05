package gateway

import (
	"context"
	"errors"
	"github.com/Just9120/llm-inspector/internal/domain"
	"net"
	"sync"
	"time"
)

const RemoteProbeSource = "remote-dns-tcp-connect-probe-v1"

type RemoteBackendStatus struct {
	Availability          string        `json:"availability"`
	Destination           string        `json:"destination"`
	NetworkConnectLatency domain.Metric `json:"network_connect_latency"`
	CheckedAt             *time.Time    `json:"checked_at"`
	Message               string        `json:"message"`
}
type networkDialer interface {
	DialContext(context.Context, string, string) (net.Conn, error)
}
type RemoteBackendMonitor struct {
	mu       sync.RWMutex
	gate     chan struct{}
	snapshot RemoteBackendStatus
	address  string
	dialer   networkDialer
}

func NewRemoteBackendMonitor(config Config) (*RemoteBackendMonitor, error) {
	return newRemoteBackendMonitor(config, &net.Dialer{Timeout: 3 * time.Second})
}
func newRemoteBackendMonitor(config Config, dialer networkDialer) (*RemoteBackendMonitor, error) {
	target, err := config.target(false)
	if err != nil || !config.Remote || dialer == nil {
		return nil, errors.New("network probe требует explicit private HTTPS remote target")
	}
	port := target.Port()
	if port == "" {
		port = "443"
	}
	return &RemoteBackendMonitor{gate: make(chan struct{}, 1), address: net.JoinHostPort(target.Hostname(), port), dialer: dialer, snapshot: RemoteBackendStatus{Availability: "unknown", Destination: target.String(), NetworkConnectLatency: domain.Missing(domain.Milliseconds, "inspector", RemoteProbeSource), Message: "Доступность remote backend ещё не проверена."}}, nil
}
func (m *RemoteBackendMonitor) Snapshot() RemoteBackendStatus {
	m.mu.RLock()
	defer m.mu.RUnlock()
	return cloneRemoteStatus(m.snapshot)
}
func cloneRemoteStatus(s RemoteBackendStatus) RemoteBackendStatus {
	s.NetworkConnectLatency = s.NetworkConnectLatency.Clone()
	if s.CheckedAt != nil {
		v := *s.CheckedAt
		s.CheckedAt = &v
	}
	return s
}
func (m *RemoteBackendMonitor) Probe(ctx context.Context) (RemoteBackendStatus, error) {
	select {
	case m.gate <- struct{}{}:
	case <-ctx.Done():
		return m.Snapshot(), ctx.Err()
	}
	defer func() { <-m.gate }()
	m.mu.Lock()
	m.snapshot.Availability = "probing"
	m.snapshot.Message = "Проверяется DNS+TCP connection…"
	m.snapshot.NetworkConnectLatency = domain.Missing(domain.Milliseconds, "inspector", RemoteProbeSource)
	m.mu.Unlock()
	probeContext, cancel := context.WithTimeout(ctx, 3*time.Second)
	defer cancel()
	start := time.Now()
	connection, err := safeDial(m.dialer, probeContext, m.address)
	elapsed := time.Since(start)
	if connection != nil {
		connection.Close()
	}
	now := time.Now().UTC()
	m.mu.Lock()
	defer m.mu.Unlock()
	m.snapshot.CheckedAt = &now
	switch {
	case ctx.Err() != nil:
		m.snapshot.Availability = "unknown"
		m.snapshot.Message = "Network probe отменён; latency недоступна."
	case err == nil && connection != nil:
		m.snapshot.Availability = "available"
		m.snapshot.NetworkConnectLatency = domain.Derived(float64(elapsed)/float64(time.Millisecond), domain.Milliseconds, domain.Calculated, RemoteProbeSource, "stopwatch-elapsed-v1")
		m.snapshot.Message = "Remote target принял DNS+TCP connection. Это не TLS, RTT или inference latency."
	default:
		m.snapshot.Availability = "unavailable"
		m.snapshot.Message = "Remote target недоступен; network latency не подтверждена."
	}
	return cloneRemoteStatus(m.snapshot), ctx.Err()
}
func safeDial(d networkDialer, ctx context.Context, address string) (c net.Conn, err error) {
	defer func() {
		if recover() != nil {
			err = errors.New("network probe failure")
		}
	}()
	return d.DialContext(ctx, "tcp", address)
}
