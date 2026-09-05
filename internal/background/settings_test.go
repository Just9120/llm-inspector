package background

import (
	"errors"
	"github.com/Just9120/llm-inspector/internal/performance"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSettingsLegacyMigrationAndDefaults(t *testing.T) {
	for _, fixture := range []string{`{"schema_version":1,"autostart_enabled":true,"notifications":{"backend_unavailable":true}}`, `{"schema_version":2,"autostart_enabled":true,"notifications":{"backend_unavailable":true}}`} {
		s, err := decodeSettings([]byte(fixture))
		if err != nil || s.SchemaVersion != 2 || !s.AutostartEnabled || !s.Notifications.BackendUnavailable || !s.Notifications.SilentMode || s.Monitoring.Profile != performance.Balanced || s.Monitoring.CustomSamplingMilliseconds != 1000 {
			t.Fatal(s, err)
		}
	}
	s := DefaultSettings()
	if s.AutostartEnabled || s.Notifications.BackendUnavailable || s.Notifications.LongOperationCompleted || s.Notifications.RecurringError || s.Notifications.HighContextUsage || !s.Notifications.SilentMode {
		t.Fatal("unsafe defaults")
	}
}
func TestSettingsInvalidDocumentsArePreserved(t *testing.T) {
	for _, fixture := range []string{`{`, `null`, `{}`, `{"schema_version":3}`, `{"schema_version":1}`, `{"schema_version":1,"notifications":{},"monitoring":{}}`, `{"schema_version":2,"unknown":1}`, `{"schema_version":2,"notifications":null}`, `{"schema_version":2,"monitoring":null}`, `{"schema_version":2,"monitoring":{"profile":"alien"}}`, `{"schema_version":2,"monitoring":{"custom_sampling_interval_milliseconds":249}}`, `{"schema_version":2,"notifications":{"unknown":true}}`, `{"schema_version":2} {}`, strings.Repeat(" ", maximumSettingsBytes+1)} {
		path := filepath.Join(t.TempDir(), "settings.json")
		if err := os.WriteFile(path, []byte(fixture), 0600); err != nil {
			t.Fatal(err)
		}
		store, _ := NewSettingsStore(path)
		if _, err := store.Load(); err == nil {
			t.Fatal("bad input accepted", fixture)
		}
		if store.Save(DefaultSettings()) == nil {
			t.Fatal("bad input overwritten")
		}
		got, _ := os.ReadFile(path)
		if string(got) != fixture {
			t.Fatal("original altered")
		}
	}
}
func TestSettingsAtomicSaveReloadAndPathGuards(t *testing.T) {
	path := filepath.Join(t.TempDir(), "nested", "settings.json")
	store, err := NewSettingsStore(path)
	if err != nil {
		t.Fatal(err)
	}
	s, err := store.Load()
	if err != nil || s != DefaultSettings() {
		t.Fatal(s, err)
	}
	s.Monitoring.Profile = performance.Custom
	s.Monitoring.CustomSamplingMilliseconds = 250
	s.Notifications.SilentMode = false
	s.Notifications.RecurringError = true
	if err = store.Save(s); err != nil {
		t.Fatal(err)
	}
	s.Monitoring.CustomSamplingMilliseconds = 10000
	if err = store.Save(s); err != nil {
		t.Fatal(err)
	}
	got, err := store.Load()
	if err != nil || got != s {
		t.Fatal(got, err)
	}
	entries, _ := os.ReadDir(filepath.Dir(path))
	if len(entries) != 1 {
		t.Fatal("temporary files leaked")
	}
	for _, p := range []string{"relative.json", `\\server\share\settings.json`, filepath.Join(t.TempDir(), "settings.txt")} {
		if _, err = NewSettingsStore(p); err == nil {
			t.Fatal("path accepted", p)
		}
	}
	invalid := s
	invalid.Monitoring.CustomSamplingMilliseconds = 10001
	if store.Save(invalid) == nil {
		t.Fatal("bad interval saved")
	}
}

type fakeStore struct {
	value            Settings
	loadErr, saveErr error
	saves            int
}

func (s *fakeStore) Load() (Settings, error) { return s.value, s.loadErr }
func (s *fakeStore) Save(v Settings) error {
	s.saves++
	if s.saveErr != nil {
		return s.saveErr
	}
	s.value = v
	return nil
}

type fakeAutostart struct {
	enabled           bool
	reads             int
	writes            []bool
	readErr, writeErr error
}

func (a *fakeAutostart) IsEnabled() (bool, error) { a.reads++; return a.enabled, a.readErr }
func (a *fakeAutostart) SetEnabled(v bool) error {
	a.writes = append(a.writes, v)
	if a.writeErr != nil {
		return a.writeErr
	}
	a.enabled = v
	return nil
}
func TestSettingsServiceActualAutostartAndRollback(t *testing.T) {
	store := &fakeStore{value: DefaultSettings()}
	auto := &fakeAutostart{enabled: true}
	service := NewSettingsService(store, auto)
	if err := service.Initialize(); err != nil || !service.Current().AutostartEnabled || len(auto.writes) != 0 {
		t.Fatal("load changed registration or trusted stale JSON")
	}
	next := service.Current()
	next.AutostartEnabled = false
	store.saveErr = errors.New("disk full")
	if service.Save(next) == nil || !auto.enabled || !service.Current().AutostartEnabled || len(auto.writes) != 2 || auto.writes[0] || !auto.writes[1] {
		t.Fatal("rollback failed")
	}
	store.saveErr = nil
	if err := service.Save(next); err != nil || auto.enabled || service.Current() != next {
		t.Fatal("save failed", err)
	}
	auto.writeErr = errors.New("denied")
	next.AutostartEnabled = true
	before := store.saves
	if service.Save(next) == nil || store.saves != before {
		t.Fatal("autostart error ignored")
	}
	auto.readErr = errors.New("read denied")
	if service.Save(next) == nil {
		t.Fatal("read error ignored")
	}
}
func TestSettingsServiceCorruptFallbackDoesNotWrite(t *testing.T) {
	store := &fakeStore{loadErr: errors.New("corrupt")}
	auto := &fakeAutostart{enabled: true}
	service := NewSettingsService(store, auto)
	if service.Initialize() == nil || !service.Current().AutostartEnabled || !service.Current().Notifications.SilentMode || store.saves != 0 || len(auto.writes) != 0 {
		t.Fatal("fallback mutation")
	}
}
