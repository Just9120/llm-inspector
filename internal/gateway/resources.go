package gateway

import (
	"errors"
	"io"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type monitorHolder struct{ monitor domain.ResourceMonitor }

func (g *Gateway) SetRuntimeFacts(facts domain.RuntimeFacts) error {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.server != nil || !facts.Valid() {
		return errors.New("runtime facts требуют валидного contract и остановленного proxy")
	}
	g.facts.Store(&facts)
	return nil
}

func (g *Gateway) SetResourceMonitor(m domain.ResourceMonitor) error {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.server != nil {
		return errors.New("изменение collectors требует остановленного proxy")
	}
	if m == nil {
		g.monitor.Store(nil)
	} else {
		g.monitor.Store(&monitorHolder{monitor: m})
	}
	return nil
}
func (g *Gateway) startResources(c domain.RequestResourceContext) (s domain.ResourceSession) {
	defer func() {
		if recover() != nil {
			s = nil
		}
	}()
	if m := g.monitor.Load(); m != nil {
		return m.monitor.Start(c)
	}
	return nil
}
func resourceCall(fn func()) { defer func() { _ = recover() }(); fn() }

type resourceBody struct {
	io.ReadCloser
	session domain.ResourceSession
}

func (b *resourceBody) Read(p []byte) (int, error) {
	n, err := b.ReadCloser.Read(p)
	if n > 0 {
		resourceCall(func() { b.session.AddSent(n) })
	}
	return n, err
}
