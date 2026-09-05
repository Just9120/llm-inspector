package background

import (
	"bytes"
	"encoding/json"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"github.com/Just9120/llm-inspector/internal/performance"
)

type NotificationSettings struct {
	BackendUnavailable     bool `json:"backend_unavailable"`
	LongOperationCompleted bool `json:"long_operation_completed"`
	RecurringError         bool `json:"recurring_error"`
	HighContextUsage       bool `json:"high_context_usage"`
	SilentMode             bool `json:"silent_mode"`
}
type MonitoringSettings struct {
	Profile                    performance.ProfileID `json:"profile"`
	CustomSamplingMilliseconds int                   `json:"custom_sampling_interval_milliseconds"`
}
type Settings struct {
	SchemaVersion    int                  `json:"schema_version"`
	AutostartEnabled bool                 `json:"autostart_enabled"`
	Notifications    NotificationSettings `json:"notifications"`
	Monitoring       MonitoringSettings   `json:"monitoring"`
}

func DefaultSettings() Settings {
	return Settings{SchemaVersion: 2, Notifications: NotificationSettings{SilentMode: true}, Monitoring: MonitoringSettings{Profile: performance.Balanced, CustomSamplingMilliseconds: 1000}}
}
func (s Settings) Validate() error {
	if s.SchemaVersion != 2 {
		return errors.New("версия настроек не поддерживается")
	}
	if _, err := performance.Resolve(performance.Custom, s.Monitoring.CustomSamplingMilliseconds); err != nil {
		return err
	}
	_, err := performance.Resolve(s.Monitoring.Profile, s.Monitoring.CustomSamplingMilliseconds)
	return err
}

type SettingsStore interface {
	Load() (Settings, error)
	Save(Settings) error
}
type JSONSettingsStore struct {
	path string
	mu   sync.Mutex
}

func NewSettingsStore(path string) (*JSONSettingsStore, error) {
	if !filepath.IsAbs(path) || strings.HasPrefix(path, `\\`) || !strings.EqualFold(filepath.Ext(path), ".json") {
		return nil, errors.New("нужен абсолютный локальный путь к JSON-настройкам")
	}
	return &JSONSettingsStore{path: filepath.Clean(path)}, nil
}

const maximumSettingsBytes = 16384

func (s *JSONSettingsStore) Load() (Settings, error) {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.load()
}
func (s *JSONSettingsStore) load() (Settings, error) {
	f, err := os.Open(s.path)
	if errors.Is(err, os.ErrNotExist) {
		return DefaultSettings(), nil
	}
	if err != nil {
		return Settings{}, errors.New("не удалось прочитать настройки")
	}
	defer f.Close()
	data, err := io.ReadAll(io.LimitReader(f, maximumSettingsBytes+1))
	if err != nil || len(data) > maximumSettingsBytes {
		return Settings{}, errors.New("не удалось прочитать допустимый размер настроек")
	}
	return decodeSettings(data)
}
func decodeSettings(data []byte) (Settings, error) {
	defaults := DefaultSettings()
	var version struct {
		SchemaVersion int `json:"schema_version"`
	}
	invalid := errors.New("настройки повреждены или имеют неподдерживаемую структуру; исходный файл сохранён")
	if json.Unmarshal(data, &version) != nil || version.SchemaVersion < 1 || version.SchemaVersion > 2 {
		return Settings{}, invalid
	}
	// Pointers reject explicit null while preserving legacy defaults for omitted
	// optional v2 properties. v1 notifications remain required.
	wire := struct {
		SchemaVersion    int                   `json:"schema_version"`
		AutostartEnabled bool                  `json:"autostart_enabled"`
		Notifications    *NotificationSettings `json:"notifications"`
		Monitoring       *MonitoringSettings   `json:"monitoring,omitempty"`
	}{Notifications: &defaults.Notifications, Monitoring: &defaults.Monitoring}
	if version.SchemaVersion == 1 {
		legacy := struct {
			SchemaVersion    int                   `json:"schema_version"`
			AutostartEnabled bool                  `json:"autostart_enabled"`
			Notifications    *NotificationSettings `json:"notifications"`
		}{Notifications: &defaults.Notifications}
		var fields map[string]json.RawMessage
		_ = json.Unmarshal(data, &fields)
		if strictDecode(data, &legacy) != nil || legacy.Notifications == nil || fields["notifications"] == nil {
			return Settings{}, invalid
		}
		defaults.AutostartEnabled = legacy.AutostartEnabled
		defaults.Notifications = *legacy.Notifications
	} else {
		if strictDecode(data, &wire) != nil || wire.Notifications == nil || wire.Monitoring == nil {
			return Settings{}, invalid
		}
		defaults.AutostartEnabled = wire.AutostartEnabled
		defaults.Notifications = *wire.Notifications
		defaults.Monitoring = *wire.Monitoring
	}
	if defaults.Validate() != nil {
		return Settings{}, invalid
	}
	return defaults, nil
}
func strictDecode(data []byte, value any) error {
	d := json.NewDecoder(bytes.NewReader(data))
	d.DisallowUnknownFields()
	if err := d.Decode(value); err != nil {
		return err
	}
	if d.Decode(new(any)) != io.EOF {
		return errors.New("лишние данные после JSON")
	}
	return nil
}
func (s *JSONSettingsStore) Save(settings Settings) error {
	if err := settings.Validate(); err != nil {
		return err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	// Never silently replace a newer/corrupted file, even if the UI started with
	// a safe in-memory fallback. Explicit recovery is a separate user operation.
	if _, err := s.load(); err != nil {
		return err
	}
	data, err := json.MarshalIndent(settings, "", "  ")
	if err != nil {
		return err
	}
	if err = os.MkdirAll(filepath.Dir(s.path), 0700); err != nil {
		return errors.New("не удалось создать каталог настроек")
	}
	f, err := os.CreateTemp(filepath.Dir(s.path), ".settings-*.tmp")
	if err != nil {
		return errors.New("не удалось создать временный файл настроек")
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if _, err = f.Write(data); err == nil {
		err = f.Sync()
	}
	closeErr := f.Close()
	if err != nil || closeErr != nil {
		return errors.New("не удалось записать настройки")
	}
	if err = os.Rename(tmp, s.path); err != nil {
		return errors.New("не удалось заменить файл настроек")
	}
	return nil
}

type Autostart interface {
	IsEnabled() (bool, error)
	SetEnabled(bool) error
}

// Native registration can preserve the exact previous command during rollback,
// including a registration from an older portable executable location.
type transactionalAutostart interface {
	RollbackForChange(bool) (func() error, error)
}
type SettingsService struct {
	mu        sync.Mutex
	store     SettingsStore
	autostart Autostart
	current   Settings
}

func NewSettingsService(store SettingsStore, autostart Autostart) *SettingsService {
	return &SettingsService{store: store, autostart: autostart, current: DefaultSettings()}
}
func (s *SettingsService) Current() Settings { s.mu.Lock(); defer s.mu.Unlock(); return s.current }
func (s *SettingsService) Initialize() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	settings, err := s.store.Load()
	enabled, autoErr := s.autostart.IsEnabled()
	if err != nil {
		settings = DefaultSettings()
	}
	settings.AutostartEnabled = enabled
	s.current = settings
	return errors.Join(err, autoErr)
}
func (s *SettingsService) Save(settings Settings) error {
	if err := settings.Validate(); err != nil {
		return err
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	before, err := s.autostart.IsEnabled()
	if err != nil {
		return err
	}
	changed := before != settings.AutostartEnabled
	rollback := func() error { return s.autostart.SetEnabled(before) }
	if changed {
		if registration, ok := s.autostart.(transactionalAutostart); ok {
			rollback, err = registration.RollbackForChange(settings.AutostartEnabled)
			if err != nil {
				return err
			}
		}
		if err = s.autostart.SetEnabled(settings.AutostartEnabled); err != nil {
			return err
		}
	}
	if err = s.store.Save(settings); err != nil {
		if changed {
			err = errors.Join(err, rollback())
		}
		return err
	}
	s.current = settings
	return nil
}
