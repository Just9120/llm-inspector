package background

import (
	"encoding/hex"
	"errors"
	"fmt"
	"math"
	"strings"
	"sync"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Event string

const (
	BackendUnavailable     Event = "backend_unavailable"
	LongOperationCompleted Event = "long_operation_completed"
	RecurringError         Event = "recurring_error"
	HighContextUsage       Event = "high_context_usage"
)
const NotificationPolicyVersion = "notification-policy-v1"
const DuplicateWindow = 15 * time.Minute
const RateWindow = 10 * time.Minute
const RateLimit = 3

type Candidate struct {
	Event                                Event
	EventKey                             string
	RequestID                            string
	ErrorType                            string
	Occurrences                          int
	DurationSeconds, ContextUsagePercent float64
}
type Notification struct {
	Event  Event  `json:"event"`
	Title  string `json:"title"`
	Body   string `json:"body"`
	Silent bool   `json:"silent"`
}
type Publisher interface{ Publish(Notification) error }
type DispatchResult string

const (
	Published           DispatchResult = "published"
	Disabled            DispatchResult = "disabled"
	Paused              DispatchResult = "paused"
	DuplicateSuppressed DispatchResult = "duplicate_suppressed"
	RateLimited         DispatchResult = "rate_limited"
	PublishFailed       DispatchResult = "publish_failed"
	InvalidCandidate    DispatchResult = "invalid_candidate"
)

type Decision struct {
	Event         Event          `json:"event"`
	Result        DispatchResult `json:"result"`
	PolicyVersion string         `json:"policy_version"`
}

func (s NotificationSettings) enabled(event Event) bool {
	switch event {
	case BackendUnavailable:
		return s.BackendUnavailable
	case LongOperationCompleted:
		return s.LongOperationCompleted
	case RecurringError:
		return s.RecurringError
	case HighContextUsage:
		return s.HighContextUsage
	}
	return false
}
func normalizeError(value string) string {
	switch value {
	case "client_cancellation":
		return "client_cancelled"
	case "relay_failure", "inspector_failure":
		return "relay_failed"
	case "none", "backend_unavailable", "client_cancelled", "relay_failed", "connection_refused", "model_loading", "http_error", "timeout", "context_overflow", "backend_crash":
		return value
	}
	return ""
}
func requestKey(value string) string {
	if len(value) == 36 {
		if value[8] != '-' || value[13] != '-' || value[18] != '-' || value[23] != '-' {
			return ""
		}
		value = strings.ReplaceAll(value, "-", "")
	}
	if len(value) != 32 || value == strings.Repeat("0", 32) {
		return ""
	}
	if _, err := hex.DecodeString(value); err != nil {
		return ""
	}
	return strings.ToLower(value)
}
func EvaluateNotifications(obs domain.Observation, occurrences int) []Candidate {
	result := []Candidate{}
	id := requestKey(obs.RequestID)
	if id == "" {
		return result
	}
	errorType := normalizeError(obs.ErrorType)
	switch errorType {
	case "backend_unavailable", "connection_refused", "model_loading", "timeout", "backend_crash", "relay_failed":
		result = append(result, Candidate{Event: BackendUnavailable, EventKey: id, RequestID: id, ErrorType: errorType})
	}
	if errorType == "none" && obs.Outcome == "completed" && obs.DurationMS >= 60000 && !math.IsNaN(obs.DurationMS) && !math.IsInf(obs.DurationMS, 0) {
		result = append(result, Candidate{Event: LongOperationCompleted, EventKey: id, RequestID: id, DurationSeconds: obs.DurationMS / 1000})
	}
	if errorType != "" && errorType != "none" && occurrences >= 2 {
		result = append(result, Candidate{Event: RecurringError, EventKey: errorType, ErrorType: errorType, Occurrences: occurrences})
	}
	used, limit := obs.Telemetry.ContextUsage, obs.Telemetry.ContextLimit
	validCount := func(m domain.Metric) bool {
		return m.Unit == domain.Tokens && m.Validate() == nil && m.Value != nil && (m.Quality == domain.Exact || m.Quality == domain.Calculated)
	}
	if validCount(used) && validCount(limit) && *limit.Value > 0 && *used.Value <= *limit.Value {
		percent := 100 * (*used.Value / *limit.Value)
		if percent >= 90 {
			result = append(result, Candidate{Event: HighContextUsage, EventKey: id, RequestID: id, ContextUsagePercent: percent})
		}
	}
	return result
}
func FormatNotification(c Candidate, silent bool) (Notification, error) {
	invalid := errors.New("неподтверждённые данные уведомления")
	if len(c.EventKey) == 0 || len(c.EventKey) > 128 {
		return Notification{}, invalid
	}
	for _, r := range c.EventKey {
		if !(r >= 'a' && r <= 'z' || r >= 'A' && r <= 'Z' || r >= '0' && r <= '9' || r == '-' || r == '_' || r == '.') {
			return Notification{}, invalid
		}
	}
	n := Notification{Event: c.Event, Silent: silent}
	id := requestKey(c.RequestID)
	errorType := normalizeError(c.ErrorType)
	if c.Event != RecurringError && id == "" {
		return Notification{}, invalid
	}
	switch c.Event {
	case BackendUnavailable:
		if errorType == "" || errorType == "none" {
			return Notification{}, invalid
		}
		n.Title = "LLM backend недоступен"
		n.Body = fmt.Sprintf("Запрос %s завершён: %s.", id[:8], errorType)
	case LongOperationCompleted:
		if c.DurationSeconds < 0 || math.IsNaN(c.DurationSeconds) || math.IsInf(c.DurationSeconds, 0) {
			return Notification{}, invalid
		}
		n.Title = "Длительная операция завершена"
		n.Body = fmt.Sprintf("Запрос %s выполнен за %.1f с.", id[:8], c.DurationSeconds)
	case RecurringError:
		if errorType == "" || errorType == "none" || c.Occurrences < 2 {
			return Notification{}, invalid
		}
		n.Title = "Повторяющаяся ошибка LLM"
		n.Body = fmt.Sprintf("Ошибка %s повторилась %d раз за текущий запуск приложения.", errorType, c.Occurrences)
	case HighContextUsage:
		if c.ContextUsagePercent < 0 || c.ContextUsagePercent > 100 || math.IsNaN(c.ContextUsagePercent) {
			return Notification{}, invalid
		}
		n.Title = "Контекст почти заполнен"
		n.Body = fmt.Sprintf("Запрос %s использует %.1f%% подтверждённого лимита контекста.", id[:8], c.ContextUsagePercent)
	default:
		return Notification{}, invalid
	}
	return n, nil
}

type Dispatcher struct {
	mu        sync.Mutex
	publisher Publisher
	paused    bool
	byKey     map[string]time.Time
	published []time.Time
}

func NewDispatcher(p Publisher) *Dispatcher {
	return &Dispatcher{publisher: p, byKey: map[string]time.Time{}, published: []time.Time{}}
}
func (d *Dispatcher) IsPaused() bool { d.mu.Lock(); defer d.mu.Unlock(); return d.paused }
func (d *Dispatcher) TogglePaused() bool {
	d.mu.Lock()
	defer d.mu.Unlock()
	d.paused = !d.paused
	return d.paused
}
func (d *Dispatcher) Dispatch(candidates []Candidate, settings NotificationSettings, now time.Time) []Decision {
	d.mu.Lock()
	defer d.mu.Unlock()
	for len(d.published) > 0 && now.Sub(d.published[0]) >= RateWindow {
		d.published = d.published[1:]
	}
	for key, at := range d.byKey {
		if now.Sub(at) >= DuplicateWindow {
			delete(d.byKey, key)
		}
	}
	results := make([]Decision, 0, len(candidates))
	for _, c := range candidates {
		n, err := FormatNotification(c, settings.SilentMode)
		key := string(c.Event) + ":" + c.EventKey
		result := Published
		at, seen := d.byKey[key]
		switch {
		case err != nil:
			result = InvalidCandidate
		case !settings.enabled(c.Event):
			result = Disabled
		case d.paused:
			result = Paused
		case seen && now.Sub(at) < DuplicateWindow:
			result = DuplicateSuppressed
		case len(d.published) >= RateLimit:
			result = RateLimited
		}
		if result == Published {
			if safePublish(d.publisher, n) != nil {
				result = PublishFailed
			} else {
				d.byKey[key] = now
				d.published = append(d.published, now)
			}
		}
		results = append(results, Decision{c.Event, result, NotificationPolicyVersion})
	}
	return results
}
func safePublish(p Publisher, n Notification) (err error) {
	defer func() {
		if recover() != nil {
			err = errors.New("сбой системного уведомления")
		}
	}()
	if p == nil {
		return errors.New("системные уведомления недоступны")
	}
	return p.Publish(n)
}
