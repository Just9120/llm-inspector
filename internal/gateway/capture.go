package gateway

import (
	"io"
	"sync"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/telemetry"
)

type requestCapture struct {
	inner          io.ReadCloser
	mu             sync.Mutex
	session        *telemetry.RequestSession
	expected, read int64
	eof            bool
}

func (c *requestCapture) Read(b []byte) (int, error) {
	n, err := c.inner.Read(b)
	c.mu.Lock()
	c.session.Observe(b[:n])
	c.read += int64(n)
	c.eof = c.eof || err == io.EOF
	c.mu.Unlock()
	return n, err
}
func (c *requestCapture) Close() error { return c.inner.Close() }
func (c *requestCapture) Result() (domain.Metric, *int) {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.session.Complete(c.eof || (c.expected >= 0 && c.read == c.expected))
}
