package history

import (
	"context"
	"sync"
	"sync/atomic"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Buffered struct {
	store        *Store
	observations chan domain.Observation
	resources    chan []domain.ResourceSample
	closed       chan struct{}
	done         chan struct{}
	once         sync.Once
	mu           sync.Mutex
	ctx          context.Context
	cancel       context.CancelFunc
	failed       atomic.Uint64
	dropped      atomic.Uint64
	written      atomic.Uint64
}
type BufferHealth struct {
	Failed  uint64 `json:"failed"`
	Dropped uint64 `json:"dropped"`
	Written uint64 `json:"written"`
}

// NewBuffered has fixed capacity and never retries failed writes on the relay
// path. Caller stops all gateway/resource producers before Close; the store is
// closed only after the bounded drain. Health reports counts, never SQL text.
func NewBuffered(s *Store) *Buffered {
	b := &Buffered{store: s, observations: make(chan domain.Observation, 256), resources: make(chan []domain.ResourceSample, 16), closed: make(chan struct{}), done: make(chan struct{})}
	b.ctx, b.cancel = context.WithCancel(context.Background())
	go b.run()
	return b
}
func (b *Buffered) Observations() chan<- domain.Observation { return b.observations }
func (b *Buffered) OfferResources(samples []domain.ResourceSample) bool {
	b.mu.Lock()
	defer b.mu.Unlock()
	if len(samples) > 256 {
		b.dropped.Add(uint64(len(samples)))
		return false
	}
	copySamples := make([]domain.ResourceSample, len(samples))
	for i, r := range samples {
		copySamples[i] = r
		if r.Stage != nil {
			v := *r.Stage
			copySamples[i].Stage = &v
		}
		if r.Process != nil {
			v := *r.Process
			copySamples[i].Process = &v
		}
		for _, f := range resourceFields(&copySamples[i]) {
			*f.value = f.value.Clone()
		}
	}
	select {
	case <-b.closed:
		b.dropped.Add(uint64(len(samples)))
		return false
	default:
	}
	select {
	case b.resources <- copySamples:
		return true
	default:
		b.dropped.Add(uint64(len(samples)))
		return false
	}
}
func (b *Buffered) Health() BufferHealth {
	return BufferHealth{Failed: b.failed.Load(), Dropped: b.dropped.Load(), Written: b.written.Load()}
}
func (b *Buffered) Close(ctx context.Context) error {
	b.once.Do(func() { b.mu.Lock(); defer b.mu.Unlock(); close(b.closed) })
	select {
	case <-b.done:
		return nil
	case <-ctx.Done():
		b.cancel()
		return ctx.Err()
	}
}
func (b *Buffered) writeObservation(o domain.Observation) {
	ctx, cancel := context.WithTimeout(b.ctx, 5*time.Second)
	defer cancel()
	if err := b.store.Record(ctx, o); err != nil {
		b.failed.Add(1)
	} else {
		b.written.Add(1)
	}
}
func (b *Buffered) writeResources(r []domain.ResourceSample) {
	ctx, cancel := context.WithTimeout(b.ctx, 5*time.Second)
	defer cancel()
	if err := b.store.RecordResources(ctx, r); err != nil {
		b.failed.Add(uint64(len(r)))
	} else {
		b.written.Add(uint64(len(r)))
	}
}
func (b *Buffered) run() {
	defer close(b.done)
	defer b.cancel()
	for {
		select {
		case o := <-b.observations:
			b.writeObservation(o)
		case r := <-b.resources:
			// Producers submit terminal samples after their observation. Drain
			// already-enqueued observations before resolving resource FKs.
			for i, n := 0, len(b.observations); i < n; i++ {
				b.writeObservation(<-b.observations)
			}
			b.writeResources(r)
		case <-b.closed:
			// Prefer request commits before the final resource drain so available
			// foreign-key correlation survives shutdown.
			for {
				select {
				case o := <-b.observations:
					b.writeObservation(o)
				default:
					goto resources
				}
			}
		resources:
			for {
				select {
				case r := <-b.resources:
					b.writeResources(r)
				default:
					return
				}
			}
		}
	}
}
