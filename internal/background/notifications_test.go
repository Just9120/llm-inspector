package background

import (
	"context"
	"errors"
	"fmt"
	"github.com/Just9120/llm-inspector/internal/domain"
	"math"
	"strings"
	"testing"
	"time"
)

type publisherFunc func(Notification) error

func (f publisherFunc) Publish(n Notification) error { return f(n) }
func notificationObservation() domain.Observation {
	return domain.Observation{RequestID: strings.Repeat("1", 32), DurationMS: 60000, Outcome: "completed", ErrorType: "none", Telemetry: domain.MissingTelemetry(domain.Ollama)}
}
func allNotifications() NotificationSettings {
	return NotificationSettings{true, true, true, true, true}
}
func TestNotificationRulesThresholdsQualityAndPrivacy(t *testing.T) {
	obs := notificationObservation()
	obs.Telemetry.ContextUsage = domain.Measured(90, domain.Tokens, "backend_extension", "v1")
	obs.Telemetry.ContextLimit = domain.Measured(100, domain.Tokens, "backend_extension", "v1")
	c := EvaluateNotifications(obs, 0)
	if len(c) != 2 || c[0].Event != LongOperationCompleted || c[1].Event != HighContextUsage {
		t.Fatal(c)
	}
	obs.DurationMS = 59999
	obs.Telemetry.ContextUsage = domain.Measured(89, domain.Tokens, "backend_extension", "v1")
	if len(EvaluateNotifications(obs, 0)) != 0 {
		t.Fatal("threshold ignored")
	}
	for _, m := range []domain.Metric{domain.Derived(95, domain.Tokens, domain.Estimated, "v1", "estimate"), domain.Measured(101, domain.Tokens, "backend_extension", "v1"), domain.Measured(95, domain.Bytes, "backend_extension", "v1"), domain.Missing(domain.Tokens, "backend_extension", "v1")} {
		obs.Telemetry.ContextUsage = m
		if len(EvaluateNotifications(obs, 0)) != 0 {
			t.Fatal("unconfirmed context")
		}
	}
	obs.ErrorType = "backend_crash"
	c = EvaluateNotifications(obs, 2)
	if len(c) != 2 || c[0].Event != BackendUnavailable || c[1].Event != RecurringError {
		t.Fatal(c)
	}
	obs.ErrorType = "PRIVATE_ERROR_TEXT"
	obs.Telemetry.Model = "PRIVATE_MODEL"
	if len(EvaluateNotifications(obs, 999)) != 0 {
		t.Fatal("raw error accepted")
	}
	for _, v := range []float64{math.NaN(), math.Inf(1)} {
		obs.ErrorType = "none"
		obs.DurationMS = v
		if len(EvaluateNotifications(obs, 0)) != 0 {
			t.Fatal("invalid duration")
		}
	}
	for _, id := range []string{"private text", strings.Repeat("0", 32), strings.Repeat("a", 31), "11111111-1111-1111-1111_111111111111"} {
		obs.RequestID = id
		obs.ErrorType = "backend_unavailable"
		if len(EvaluateNotifications(obs, 2)) != 0 {
			t.Fatal("invalid identity accepted")
		}
	}
}
func TestNotificationDedupRateAndPauseBoundaries(t *testing.T) {
	var sent []Notification
	d := NewDispatcher(publisherFunc(func(n Notification) error { sent = append(sent, n); return nil }))
	settings := allNotifications()
	at := time.Now()
	c := EvaluateNotifications(notificationObservation(), 0)[0]
	dispatch := func(candidate Candidate, when time.Time) DispatchResult {
		return d.Dispatch([]Candidate{candidate}, settings, when)[0].Result
	}
	if dispatch(c, at) != Published || dispatch(c, at.Add(15*time.Minute-time.Nanosecond)) != DuplicateSuppressed || dispatch(c, at.Add(15*time.Minute)) != Published {
		t.Fatal("dedup boundary")
	}
	at = at.Add(time.Hour)
	for i := 0; i < 3; i++ {
		c.EventKey = fmt.Sprintf("event%d", i)
		if dispatch(c, at) != Published {
			t.Fatal("rate early")
		}
	}
	c.EventKey = "overflow"
	if dispatch(c, at.Add(10*time.Minute-time.Nanosecond)) != RateLimited || dispatch(c, at.Add(10*time.Minute)) != Published {
		t.Fatal("rate boundary")
	}
	d.TogglePaused()
	c.EventKey = "paused"
	if !d.IsPaused() || dispatch(c, at.Add(time.Hour)) != Paused {
		t.Fatal("pause")
	}
	d.TogglePaused()
	settings.LongOperationCompleted = false
	if dispatch(c, at.Add(time.Hour)) != Disabled {
		t.Fatal("disabled")
	}
	for _, n := range sent {
		if !n.Silent || !strings.Contains(n.Title, "завершена") {
			t.Fatal("language/silent contract")
		}
	}
	if len(d.byKey) > 6 {
		t.Fatal("unbounded dedup state")
	}
}
func TestNotificationPublishFailureAndInvalidCandidate(t *testing.T) {
	c := EvaluateNotifications(notificationObservation(), 0)[0]
	for _, publisher := range []Publisher{nil, publisherFunc(func(Notification) error { return errors.New("native failure") }), publisherFunc(func(Notification) error { panic("native panic") })} {
		d := NewDispatcher(publisher)
		if d.Dispatch([]Candidate{c}, allNotifications(), time.Now())[0].Result != PublishFailed || len(d.byKey) != 0 {
			t.Fatal("failure counted as delivered")
		}
	}
	for _, bad := range []Candidate{{Event: BackendUnavailable, EventKey: "safe", RequestID: c.RequestID, ErrorType: "PRIVATE_TEXT"}, {Event: LongOperationCompleted, EventKey: "safe", RequestID: c.RequestID, DurationSeconds: math.Inf(1)}, {Event: RecurringError, EventKey: "unsafe/value", ErrorType: "timeout", Occurrences: 2}, {Event: HighContextUsage, EventKey: "safe", RequestID: c.RequestID, ContextUsagePercent: 101}} {
		if _, err := FormatNotification(bad, true); err == nil {
			t.Fatal("invalid candidate")
		}
	}
}
func TestNotificationMonitorBoundedDrainAndIsolatedFailure(t *testing.T) {
	entered, release := make(chan struct{}), make(chan struct{})
	calls := 0
	d := NewDispatcher(publisherFunc(func(Notification) error {
		calls++
		if calls == 1 {
			close(entered)
			<-release
		}
		return errors.New("native unavailable")
	}))
	m := NewNotificationMonitor(allNotifications, d)
	obs := notificationObservation()
	m.Offer(obs)
	select {
	case <-entered:
	case <-time.After(time.Second):
		t.Fatal("not started")
	}
	for i := 0; i < 256; i++ {
		if !m.Offer(obs) {
			t.Fatal("early overflow")
		}
	}
	if m.Offer(obs) || m.Health().Dropped != 1 {
		t.Fatal("unbounded queue")
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if m.Close(ctx) == nil {
		t.Fatal("blocked publisher close not bounded")
	}
	close(release)
	ctx, cancel = context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if err := m.Close(ctx); err != nil {
		t.Fatal(err)
	}
	if m.Health().Failures != 257 || m.Offer(obs) {
		t.Fatal(m.Health())
	}
}
func TestBackgroundLifetimeAndTrayRouting(t *testing.T) {
	l := &Lifetime{BackgroundAvailable: true}
	if l.OnClosing() != HideAndContinue {
		t.Fatal("closing stops monitoring")
	}
	l.RequestExit()
	if l.OnClosing() != ExitProcess || (&Lifetime{}).OnClosing() != ExitProcess {
		t.Fatal("exit/fallback")
	}
	var commands []string
	r := TrayCommands{Show: func(settings bool) { commands = append(commands, fmt.Sprint(settings)) }, Toggle: func() { commands = append(commands, "toggle") }, Exit: func() { commands = append(commands, "exit") }}
	for _, cmd := range []TrayCommand{OpenApplication, OpenNotificationSettings, ToggleNotifications, Exit, 9999} {
		r.Execute(cmd)
	}
	if strings.Join(commands, ",") != "false,true,toggle,exit" {
		t.Fatal(commands)
	}
}
