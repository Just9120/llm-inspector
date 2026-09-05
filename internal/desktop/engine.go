package desktop

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"path/filepath"
	"runtime"
	"strconv"
	"sync"
	"time"

	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/diagnostics"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/lifecycle"
	"github.com/Just9120/llm-inspector/internal/performance"
	"github.com/Just9120/llm-inspector/internal/remote"
	"github.com/Just9120/llm-inspector/internal/resources"
)

const Version = "1.0.0"

type Dependencies struct {
	DataDirectory string
	Settings      background.SettingsStore
	Autostart     background.Autostart
	Publisher     background.Publisher
	Credentials   remote.CredentialStore
	Probe         resources.Probe
	Resolver      resources.Resolver
	Lifecycle     lifecycle.Runtime
	OSVersion     string
}

type RuntimeStatus struct {
	ProxyRunning     bool           `json:"proxy_running"`
	Listener         string         `json:"listener"`
	BackendURL       string         `json:"backend_url"`
	Backend          domain.Backend `json:"backend"`
	Remote           bool           `json:"remote"`
	Message          string         `json:"message"`
	HistoryAvailable bool           `json:"history_available"`
	HistoryMessage   string         `json:"history_message"`
	SettingsMessage  string         `json:"settings_message"`
}
type ViewState struct {
	Version             string                        `json:"version"`
	Status              RuntimeStatus                 `json:"status"`
	Live                domain.LiveSnapshot           `json:"live"`
	ActiveCount         int64                         `json:"active_count"`
	Latest              *domain.Observation           `json:"latest"`
	Operation           *domain.OperationGraph        `json:"operation"`
	Resources           []domain.ResourceSample       `json:"resources"`
	Diagnostics         []diagnostics.Conclusion      `json:"diagnostics"`
	DiagnosticResource  *domain.ResourceSample        `json:"diagnostic_resource"`
	Hub                 HubHealth                     `json:"hub_health"`
	Writer              history.BufferHealth          `json:"writer_health"`
	Collectors          resources.Health              `json:"collector_health"`
	Notifications       background.NotificationHealth `json:"notification_health"`
	GatewayDropped      uint64                        `json:"gateway_dropped"`
	Settings            background.Settings           `json:"settings"`
	NotificationsPaused bool                          `json:"notifications_paused"`
	RemoteAccess        remote.Snapshot               `json:"remote_access"`
	RemoteBackend       *gateway.RemoteBackendStatus  `json:"remote_backend"`
	CapturedAt          time.Time                     `json:"captured_at"`
}

type Engine struct {
	mu            sync.RWMutex
	status        RuntimeStatus
	ctx           context.Context
	cancel        context.CancelFunc
	config        gateway.Config
	facts         domain.RuntimeFacts
	Gateway       *gateway.Gateway
	History       *history.Store
	Settings      *background.SettingsService
	Remote        *remote.Manager
	RemoteBackend *gateway.RemoteBackendMonitor
	Lifecycle     map[lifecycle.Backend]*lifecycle.Manager
	dispatcher    *background.Dispatcher
	notifications *background.NotificationMonitor
	monitor       *resources.Monitor
	buffer        *history.Buffered
	hub           *Hub
	closeOnce     sync.Once
	closeDone     chan struct{}
	closeError    error
}

// Start degrades optional history/settings/remote failures without opening a
// public listener, replacing corrupt data or disabling gateway observations.
func Start(config gateway.Config, d Dependencies) (*Engine, error) {
	if err := config.Validate(); err != nil {
		return nil, err
	}
	if d.Settings == nil || d.Autostart == nil || d.Publisher == nil || d.Probe == nil || d.Resolver == nil {
		return nil, errors.New("не заданы зависимости Windows runtime")
	}
	ctx, cancel := context.WithCancel(context.Background())
	e := &Engine{ctx: ctx, cancel: cancel, config: config, closeDone: make(chan struct{}), Lifecycle: map[lifecycle.Backend]*lifecycle.Manager{}}
	e.status = RuntimeStatus{Backend: config.Backend, BackendURL: config.BackendURL, Remote: config.Remote, Message: "Proxy ещё не запущен", HistoryMessage: "История недоступна; наблюдение может продолжаться"}
	e.Settings = background.NewSettingsService(d.Settings, d.Autostart)
	if err := e.Settings.Initialize(); err != nil {
		e.status.SettingsMessage = "Настройки недоступны или повреждены; применены безопасные значения, исходный файл сохранён"
	}
	e.dispatcher = background.NewDispatcher(d.Publisher)
	e.notifications = background.NewNotificationMonitor(func() background.NotificationSettings { return e.Settings.Current().Notifications }, e.dispatcher)
	if filepath.IsAbs(d.DataDirectory) {
		store, err := history.Open(ctx, filepath.Join(d.DataDirectory, "data", "inspector.db"))
		if err == nil {
			retention, readErr := store.Retention(ctx)
			if readErr == nil {
				_, readErr = store.ApplyRetention(ctx, retention, time.Now())
			}
			if readErr == nil {
				e.History = store
				e.buffer = history.NewBuffered(store)
				e.status.HistoryAvailable = true
				e.status.HistoryMessage = "Локальная техническая история доступна"
			} else {
				_ = store.Close()
			}
		}
	}
	fingerprint := sha256.Sum256([]byte(string(config.Backend) + "\x00" + config.BackendURL + "\x00" + strconv.Itoa(config.Port) + "\x00" + strconv.FormatBool(config.Remote) + "\x00backend-telemetry-v1"))
	e.facts = domain.RuntimeFacts{ConfigurationID: hex.EncodeToString(fingerprint[:]), InspectorVersion: Version, FrameworkVersion: runtime.Version(), OSVersion: domain.TechnicalIdentifier(d.OSVersion), TelemetryVersion: "backend-telemetry-v1"}
	e.hub = NewHub(e.buffer, e.notifications, e.facts)
	e.monitor = resources.NewMonitor(d.Probe, d.Resolver, func(samples []domain.ResourceSample) { e.hub.OfferResources(samples) })
	settings := e.Settings.Current()
	profile, _ := performance.Resolve(settings.Monitoring.Profile, settings.Monitoring.CustomSamplingMilliseconds)
	e.monitor.SetInterval(profile.Interval())
	proxy, err := gateway.New(config, e.hub.Observations())
	if err != nil {
		_ = e.Close(context.Background())
		return nil, err
	}
	e.Gateway = proxy
	if err = proxy.SetRuntimeFacts(e.facts); err != nil {
		_ = e.Close(context.Background())
		return nil, err
	}
	if err = proxy.SetResourceMonitor(e.monitor); err != nil {
		_ = e.Close(context.Background())
		return nil, err
	}
	if d.Credentials != nil {
		e.Remote = remote.NewManager(d.Credentials)
		_ = e.Remote.Initialize(ctx)
		if err = proxy.SetRemoteAuthorizer(e.Remote); err != nil {
			_ = e.Close(context.Background())
			return nil, err
		}
	}
	if config.Remote {
		e.RemoteBackend, _ = gateway.NewRemoteBackendMonitor(config)
	}
	if d.Lifecycle != nil {
		for _, backend := range []lifecycle.Backend{lifecycle.Ollama, lifecycle.LlamaCpp, lifecycle.LMStudio} {
			manager, err := lifecycle.NewManager(backend, d.Lifecycle, func() int { return int(e.Gateway.ActiveCount()) })
			if err == nil {
				e.Lifecycle[backend] = manager
			}
		}
	}
	listener, err := proxy.Start()
	if err != nil {
		e.status.Message = "Локальный порт proxy недоступен. Проверьте, не занят ли он другим приложением"
	} else {
		e.status.ProxyRunning = true
		e.status.Listener = listener
		e.status.Message = "Локальный proxy работает"
	}
	return e, nil
}

func (e *Engine) Context() context.Context { return e.ctx }
func (e *Engine) Snapshot() ViewState {
	e.mu.RLock()
	status := e.status
	e.mu.RUnlock()
	s := ViewState{Version: Version, Status: status, CapturedAt: time.Now(), Settings: e.Settings.Current(), NotificationsPaused: e.dispatcher.IsPaused(), Resources: []domain.ResourceSample{}, Diagnostics: []diagnostics.Conclusion{}}
	if e.Gateway != nil {
		s.Live = e.Gateway.LiveSnapshot()
		s.ActiveCount = e.Gateway.ActiveCount()
		s.GatewayDropped = e.Gateway.Dropped()
	}
	if e.hub != nil {
		s.Latest, s.Operation = e.hub.Latest()
		s.Hub = e.hub.Health()
	}
	if e.monitor != nil {
		s.Resources = e.monitor.Latest()
		s.Collectors = e.monitor.Health()
	}
	if e.buffer != nil {
		s.Writer = e.buffer.Health()
	}
	if e.notifications != nil {
		s.Notifications = e.notifications.Health()
	}
	if e.Remote != nil {
		s.RemoteAccess = e.Remote.Snapshot()
	} else {
		s.RemoteAccess = remote.Snapshot{Message: "Защищённое хранилище недоступно; remote ingress запрещён"}
	}
	if e.RemoteBackend != nil {
		value := e.RemoteBackend.Snapshot()
		s.RemoteBackend = &value
	}
	if s.Latest != nil && e.hub != nil {
		s.DiagnosticResource = e.hub.DiagnosticResource(s.Latest.RequestID)
	}
	s.Diagnostics = diagnostics.Default().Evaluate(diagnostics.Input{Latest: s.Latest, Resource: s.DiagnosticResource, Live: s.Live, CapturedAt: s.CapturedAt})
	return s
}

func (e *Engine) SaveSettings(settings background.Settings) error {
	if err := e.ctx.Err(); err != nil {
		return err
	}
	if err := e.Settings.Save(settings); err != nil {
		return err
	}
	profile, err := performance.Resolve(settings.Monitoring.Profile, settings.Monitoring.CustomSamplingMilliseconds)
	if err != nil {
		return err
	}
	e.monitor.SetInterval(profile.Interval())
	e.mu.Lock()
	e.status.SettingsMessage = "Настройки сохранены"
	e.mu.Unlock()
	return nil
}
func (e *Engine) ToggleNotifications() bool { return e.dispatcher.TogglePaused() }
func (e *Engine) Close(ctx context.Context) error {
	e.closeOnce.Do(func() {
		go func() {
			defer close(e.closeDone)
			e.cancel()
			shutdown, cancel := context.WithTimeout(context.Background(), 15*time.Second)
			defer cancel()
			var failures []error
			if e.Gateway != nil {
				failures = append(failures, e.Gateway.Stop(shutdown))
			}
			if e.monitor != nil {
				failures = append(failures, e.monitor.Close(shutdown))
			}
			if e.hub != nil {
				failures = append(failures, e.hub.Close(shutdown))
			}
			if e.notifications != nil {
				failures = append(failures, e.notifications.Close(shutdown))
			}
			if e.buffer != nil {
				failures = append(failures, e.buffer.Close(shutdown))
			}
			if e.History != nil {
				failures = append(failures, e.History.Close())
			}
			if e.Remote != nil {
				e.Remote.Close()
			}
			e.mu.Lock()
			e.status.ProxyRunning = false
			e.status.Message = "Proxy остановлен"
			e.mu.Unlock()
			e.closeError = errors.Join(failures...)
		}()
	})
	select {
	case <-e.closeDone:
		return e.closeError
	case <-ctx.Done():
		return ctx.Err()
	}
}
