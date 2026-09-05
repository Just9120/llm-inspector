package background

import (
	"context"
	"math"
	"sync"
	"sync/atomic"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type CloseAction string

const (
	HideAndContinue CloseAction = "hide_and_continue"
	ExitProcess     CloseAction = "exit_process"
)

type Lifetime struct {
	BackgroundAvailable bool
	exitRequested       atomic.Bool
}

func (l *Lifetime) RequestExit() { l.exitRequested.Store(true) }
func (l *Lifetime) OnClosing() CloseAction {
	if l.exitRequested.Load() || !l.BackgroundAvailable {
		return ExitProcess
	}
	return HideAndContinue
}

type TrayCommand int

const (
	OpenApplication TrayCommand = iota + 1001
	OpenNotificationSettings
	ToggleNotifications
	Exit
)

type TrayCommands struct {
	Show   func(settings bool)
	Toggle func()
	Exit   func()
}

func (r TrayCommands) Execute(command TrayCommand) {
	switch command {
	case OpenApplication:
		if r.Show != nil {
			r.Show(false)
		}
	case OpenNotificationSettings:
		if r.Show != nil {
			r.Show(true)
		}
	case ToggleNotifications:
		if r.Toggle != nil {
			r.Toggle()
		}
	case Exit:
		if r.Exit != nil {
			r.Exit()
		}
	}
}

// Notification worker receives only rule inputs, never request/response content,
// operation graphs or mutable collector state. There is no idle polling timer.
type NotificationMonitor struct {
	mu                sync.Mutex
	closed            bool
	queue             chan domain.Observation
	done              chan struct{}
	settings          func() NotificationSettings
	dispatcher        *Dispatcher
	dropped, failures atomic.Uint64
}
type NotificationHealth struct {
	Dropped  uint64 `json:"dropped"`
	Failures uint64 `json:"failures"`
}

func NewNotificationMonitor(settings func() NotificationSettings, dispatcher *Dispatcher) *NotificationMonitor {
	m := &NotificationMonitor{queue: make(chan domain.Observation, 256), done: make(chan struct{}), settings: settings, dispatcher: dispatcher}
	go m.run()
	return m
}
func (m *NotificationMonitor) Offer(obs domain.Observation) bool {
	input := domain.Observation{RequestID: obs.RequestID, DurationMS: obs.DurationMS, Outcome: obs.Outcome, ErrorType: normalizeError(obs.ErrorType), Telemetry: domain.Telemetry{ContextUsage: obs.Telemetry.ContextUsage.Clone(), ContextLimit: obs.Telemetry.ContextLimit.Clone()}}
	m.mu.Lock()
	defer m.mu.Unlock()
	if !m.closed {
		select {
		case m.queue <- input:
			return true
		default:
		}
	}
	m.dropped.Add(1)
	return false
}
func (m *NotificationMonitor) Health() NotificationHealth {
	return NotificationHealth{m.dropped.Load(), m.failures.Load()}
}
func (m *NotificationMonitor) Close(ctx context.Context) error {
	m.mu.Lock()
	if !m.closed {
		m.closed = true
		close(m.queue)
	}
	m.mu.Unlock()
	select {
	case <-m.done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}
func (m *NotificationMonitor) run() {
	defer close(m.done)
	counts := map[string]int{}
	for obs := range m.queue {
		func() {
			defer func() {
				if recover() != nil {
					m.failures.Add(1)
				}
			}()
			err := obs.ErrorType
			count := 0
			if err != "" && err != "none" {
				count = counts[err]
				if count < math.MaxInt32 {
					count++
				}
				counts[err] = count
			}
			for _, d := range m.dispatcher.Dispatch(EvaluateNotifications(obs, count), m.settings(), time.Now()) {
				if d.Result == PublishFailed || d.Result == InvalidCandidate {
					m.failures.Add(1)
				}
			}
		}()
	}
}
