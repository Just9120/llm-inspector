//go:build windows

package background

import (
	"errors"
	"golang.org/x/sys/windows/registry"
	"path/filepath"
	"strings"
)

const autostartKey = `Software\Microsoft\Windows\CurrentVersion\Run`
const autostartName = "LLM Inspector"

type registrationValue struct {
	value  string
	kind   uint32
	exists bool
}
type registrationStore interface {
	read() (registrationValue, error)
	write(registrationValue) error
}
type currentUserRun struct{}

func (currentUserRun) read() (registrationValue, error) {
	key, err := registry.OpenKey(registry.CURRENT_USER, autostartKey, registry.QUERY_VALUE)
	if errors.Is(err, registry.ErrNotExist) {
		return registrationValue{}, nil
	}
	if err != nil {
		return registrationValue{}, errors.New("не удалось прочитать Windows autostart")
	}
	defer key.Close()
	value, kind, err := key.GetStringValue(autostartName)
	if errors.Is(err, registry.ErrNotExist) {
		return registrationValue{}, nil
	}
	if err != nil {
		return registrationValue{}, errors.New("регистрация autostart имеет неподдерживаемый тип")
	}
	return registrationValue{value, kind, true}, nil
}
func (currentUserRun) write(value registrationValue) error {
	if !value.exists {
		key, err := registry.OpenKey(registry.CURRENT_USER, autostartKey, registry.SET_VALUE)
		if errors.Is(err, registry.ErrNotExist) {
			return nil
		}
		if err != nil {
			return errors.New("не удалось открыть Windows autostart")
		}
		defer key.Close()
		err = key.DeleteValue(autostartName)
		if err != nil && !errors.Is(err, registry.ErrNotExist) {
			return errors.New("не удалось выключить Windows autostart")
		}
		return nil
	}
	key, _, err := registry.CreateKey(registry.CURRENT_USER, autostartKey, registry.SET_VALUE)
	if err != nil {
		return errors.New("не удалось открыть Windows autostart")
	}
	defer key.Close()
	if value.kind == registry.EXPAND_SZ {
		err = key.SetExpandStringValue(autostartName, value.value)
	} else if value.kind == registry.SZ {
		err = key.SetStringValue(autostartName, value.value)
	} else {
		return errors.New("неподдерживаемый тип Windows autostart")
	}
	if err != nil {
		return errors.New("не удалось записать Windows autostart")
	}
	return nil
}

type WindowsAutostart struct {
	command string
	store   registrationStore
}

func AutostartCommand(executable string) (string, error) {
	if !filepath.IsAbs(executable) || strings.HasPrefix(executable, `\\`) || strings.ContainsAny(executable, "\"\r\n\x00") || !strings.EqualFold(filepath.Ext(executable), ".exe") {
		return "", errors.New("для autostart нужен абсолютный локальный путь к executable")
	}
	return `"` + filepath.Clean(executable) + `" --background`, nil
}
func NewWindowsAutostart(executable string) (*WindowsAutostart, error) {
	command, err := AutostartCommand(executable)
	if err != nil {
		return nil, err
	}
	return &WindowsAutostart{command, currentUserRun{}}, nil
}
func (a *WindowsAutostart) IsEnabled() (bool, error) {
	v, err := a.store.read()
	return v.exists && v.value == a.command, err
}
func (a *WindowsAutostart) SetEnabled(enabled bool) error {
	v := registrationValue{}
	if enabled {
		v = registrationValue{a.command, registry.SZ, true}
	}
	return a.store.write(v)
}
func (a *WindowsAutostart) RollbackForChange(enabled bool) (func() error, error) {
	before, err := a.store.read()
	if err != nil {
		return nil, err
	}
	expected := registrationValue{}
	if enabled {
		expected = registrationValue{a.command, registry.SZ, true}
	}
	return func() error {
		current, err := a.store.read()
		if err != nil {
			return err
		}
		if current != expected {
			return errors.New("autostart изменён внешним процессом; автоматический rollback остановлен")
		}
		return a.store.write(before)
	}, nil
}
