//go:build windows

package background

import (
	"errors"
	"golang.org/x/sys/windows/registry"
	"testing"
)

type memoryRegistration struct {
	value registrationValue
	err   error
}

func (s *memoryRegistration) read() (registrationValue, error) { return s.value, s.err }
func (s *memoryRegistration) write(v registrationValue) error {
	if s.err != nil {
		return s.err
	}
	s.value = v
	return nil
}
func TestAutostartCommandAndExactRegistration(t *testing.T) {
	command, err := AutostartCommand(`C:\Program Files\LLM Inspector\LlmInspector.exe`)
	if err != nil || command != `"C:\Program Files\LLM Inspector\LlmInspector.exe" --background` {
		t.Fatal(command, err)
	}
	for _, path := range []string{`relative.exe`, `\\server\share\app.exe`, `C:\x".exe`, `C:\x.cmd`, "C:\\x\n.exe"} {
		if _, err := AutostartCommand(path); err == nil {
			t.Fatal("unsafe path accepted")
		}
	}
	store := &memoryRegistration{}
	a := &WindowsAutostart{command, store}
	if v, _ := a.IsEnabled(); v {
		t.Fatal("default on")
	}
	a.SetEnabled(true)
	if v, _ := a.IsEnabled(); !v || store.value.kind != registry.SZ {
		t.Fatal("registration")
	}
	store.value.value += " --unrelated"
	if v, _ := a.IsEnabled(); v {
		t.Fatal("non-exact command matched")
	}
	a.SetEnabled(false)
	if store.value.exists {
		t.Fatal("disable")
	}
}
func TestAutostartRollbackPreservesOldPathAndRejectsExternalDrift(t *testing.T) {
	old := registrationValue{`"C:\Old\App.exe" --background`, registry.EXPAND_SZ, true}
	store := &memoryRegistration{value: old}
	a := &WindowsAutostart{`"C:\New\App.exe" --background`, store}
	settingsStore := &fakeStore{value: DefaultSettings(), saveErr: errors.New("disk full")}
	service := NewSettingsService(settingsStore, a)
	service.Initialize()
	next := service.Current()
	next.AutostartEnabled = true
	if service.Save(next) == nil || store.value != old {
		t.Fatal("old registration lost")
	}
	rollback, err := a.RollbackForChange(true)
	if err != nil {
		t.Fatal(err)
	}
	a.SetEnabled(true)
	store.value.value = "external change"
	if rollback() == nil || store.value.value != "external change" {
		t.Fatal("overwrote external change")
	}
	store.err = errors.New("access denied")
	if _, err := a.RollbackForChange(false); err == nil {
		t.Fatal("read failure ignored")
	}
}
