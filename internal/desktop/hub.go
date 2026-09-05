// Package desktop composes the application without depending on its web UI.
// Only the narrow Wails facade is bound; stores/runtimes are not JS endpoints.
package desktop

import (
	"context"
	"encoding/json"
	"sync"
	"sync/atomic"

	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/resources"
)

type HubHealth struct {
	Observations       uint64 `json:"observations"`
	HistoryDropped     uint64 `json:"history_dropped"`
	ResourceDropped    uint64 `json:"resource_dropped"`
	ProjectionFailures uint64 `json:"projection_failures"`
}

// Hub keeps one latest immutable technical observation. It fans out completion
// events away from HTTP forwarding, with bounded queues and no idle polling.
type Hub struct {
	mu                                                         sync.RWMutex
	latest                                                     *domain.Observation
	operation                                                  *domain.OperationGraph
	observations                                               chan domain.Observation
	resources                                                  chan []domain.ResourceSample
	closed                                                     chan struct{}
	done                                                       chan struct{}
	once                                                       sync.Once
	history                                                    *history.Buffered
	notifications                                              *background.NotificationMonitor
	facts                                                      domain.RuntimeFacts
	count, historyDropped, resourceDropped, projectionFailures atomic.Uint64
}

func NewHub(store *history.Buffered, notifications *background.NotificationMonitor, facts domain.RuntimeFacts) *Hub {
	h := &Hub{observations: make(chan domain.Observation, 256), resources: make(chan []domain.ResourceSample, 8), closed: make(chan struct{}), done: make(chan struct{}), history: store, notifications: notifications, facts: facts}
	go h.run()
	return h
}
func (h *Hub) Observations() chan<- domain.Observation { return h.observations }

// Monitor transfers its already cloned terminal timeline; no other code mutates
// that slice after offering. This avoids another large copy on collector exit.
func (h *Hub) OfferResources(samples []domain.ResourceSample) bool {
	h.mu.RLock()
	defer h.mu.RUnlock()
	if len(samples) > resources.MaximumSamplesPerRequest {
		h.resourceDropped.Add(uint64(len(samples)))
		return false
	}
	select {
	case <-h.closed:
		h.resourceDropped.Add(uint64(len(samples)))
		return false
	default:
	}
	select {
	case h.resources <- samples:
		return true
	default:
		h.resourceDropped.Add(uint64(len(samples)))
		return false
	}
}
func (h *Hub) Health() HubHealth {
	return HubHealth{h.count.Load(), h.historyDropped.Load(), h.resourceDropped.Load(), h.projectionFailures.Load()}
}
func (h *Hub) Latest() (*domain.Observation, *domain.OperationGraph) {
	h.mu.RLock()
	defer h.mu.RUnlock()
	// JSON is used only for a detached technical UI projection, never persistence
	// or an export format. Domain.Observation deliberately omits Operation.
	var observation *domain.Observation
	var operation *domain.OperationGraph
	if h.latest != nil {
		encoded, err := json.Marshal(h.latest)
		if err == nil {
			_ = json.Unmarshal(encoded, &observation)
		}
	}
	if h.operation != nil {
		encoded, err := json.Marshal(h.operation)
		if err == nil {
			_ = json.Unmarshal(encoded, &operation)
		}
	}
	return observation, operation
}
func (h *Hub) Close(ctx context.Context) error {
	h.once.Do(func() { h.mu.Lock(); defer h.mu.Unlock(); close(h.closed) })
	select {
	case <-h.done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}
func (h *Hub) observe(o domain.Observation) {
	if o.Runtime == nil && h.facts.Valid() {
		facts := h.facts
		o.Runtime = &facts
	}
	// Marshal rejects non-finite malformed data; it cannot enter the UI as a
	// zero/default metric. Gateway-created observations obey domain contracts.
	encoded, err := json.Marshal(o)
	if err != nil {
		h.projectionFailures.Add(1)
		return
	}
	var projection domain.Observation
	if json.Unmarshal(encoded, &projection) != nil {
		h.projectionFailures.Add(1)
		return
	}
	var operation *domain.OperationGraph
	if o.Operation != nil {
		encoded, err = json.Marshal(o.Operation)
		if err == nil {
			_ = json.Unmarshal(encoded, &operation)
		}
	}
	h.mu.Lock()
	h.latest = &projection
	h.operation = operation
	h.mu.Unlock()
	h.count.Add(1)
	if h.history != nil {
		select {
		case h.history.Observations() <- o:
		default:
			h.historyDropped.Add(1)
		}
	}
	if h.notifications != nil {
		h.notifications.Offer(o)
	}
}
func (h *Hub) recordResources(samples []domain.ResourceSample) {
	if h.history != nil {
		h.history.OfferResourceTimeline(samples)
	}
}
func (h *Hub) drainObservations() {
	for i, n := 0, len(h.observations); i < n; i++ {
		h.observe(<-h.observations)
	}
}
func (h *Hub) run() {
	defer close(h.done)
	for {
		select {
		case o := <-h.observations:
			h.observe(o)
		case samples := <-h.resources:
			// Gateway queues its observation before completing the resource session.
			// Preserve that order through both fanout queues so SQLite resource FKs
			// cannot race ahead of the request even under scheduler inversion.
			h.drainObservations()
			h.recordResources(samples)
		case <-h.closed:
			h.drainObservations()
			for {
				select {
				case samples := <-h.resources:
					h.recordResources(samples)
				default:
					return
				}
			}
		}
	}
}
