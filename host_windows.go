//go:build windows

package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"sync"
	"sync/atomic"
	"time"

	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/desktop"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/lifecycle"
	"github.com/Just9120/llm-inspector/internal/remote"
	"github.com/Just9120/llm-inspector/internal/resources"
	"github.com/Just9120/llm-inspector/internal/winhost"
	wailsRuntime "github.com/wailsapp/wails/v2/pkg/runtime"
)

type ShellState struct {
	Visible       bool   `json:"visible"`
	Ready         bool   `json:"ready"`
	Message       string `json:"message"`
	TrayAvailable bool   `json:"tray_available"`
	TrayFailures  uint64 `json:"tray_failures"`
	Smoke         bool   `json:"smoke"`
}

// Only GetShellState and ReportFrontendReady are exported to the JS bridge.
type Host struct {
	mu                    sync.Mutex
	startupMu             sync.Mutex
	ctx                   context.Context
	message               string
	closed                bool
	config                gateway.Config
	directory, executable string
	engine                atomic.Pointer[desktop.Engine]
	tray                  atomic.Pointer[background.WindowsTray]
	native                *lifecycle.WindowsRuntime
	exitRequested         atomic.Bool
	visible               atomic.Bool
	domLoaded             atomic.Bool
	showSettings          atomic.Bool
	facade                *desktop.Facade
	smoke                 *smokeFixture
	frontendReady         chan struct{}
	readyOnce             sync.Once
	smokePassed           atomic.Bool
}

func newHost(config gateway.Config, directory, executable string, smoke *smokeFixture) *Host {
	h := &Host{config: config, directory: directory, executable: executable, smoke: smoke, frontendReady: make(chan struct{}), message: "Подготовка локального runtime…"}
	h.facade = desktop.NewFacade(h.engine.Load, desktop.Dialogs{
		OpenExecutable: func() (string, error) {
			return h.openFile("Выберите executable backend", "Windows executable", "*.exe")
		},
		OpenModel: func() (string, error) {
			return h.openFile("Выберите установленную модель GGUF", "GGUF model", "*.gguf")
		},
		SaveJSON: func(name string) (string, error) {
			ctx := h.context()
			if ctx == nil {
				return "", desktop.ErrNotReady
			}
			return wailsRuntime.SaveFileDialog(ctx, wailsRuntime.SaveDialogOptions{Title: "Сохранить проверенный технический JSON", DefaultFilename: name, Filters: []wailsRuntime.FileFilter{{DisplayName: "JSON", Pattern: "*.json"}}, CanCreateDirectories: true})
		},
		Hide: h.hide, Exit: h.exit,
	})
	return h
}
func (h *Host) context() context.Context {
	h.mu.Lock()
	defer h.mu.Unlock()
	if h.closed {
		return nil
	}
	return h.ctx
}
func (h *Host) openFile(title, name, pattern string) (string, error) {
	ctx := h.context()
	if ctx == nil {
		return "", desktop.ErrNotReady
	}
	return wailsRuntime.OpenFileDialog(ctx, wailsRuntime.OpenDialogOptions{Title: title, Filters: []wailsRuntime.FileFilter{{DisplayName: name, Pattern: pattern}}})
}
func (h *Host) GetShellState() ShellState {
	h.mu.Lock()
	s := ShellState{Message: h.message, Ready: h.engine.Load() != nil && !h.closed, Smoke: h.smoke != nil, Visible: h.visible.Load()}
	h.mu.Unlock()
	if tray := h.tray.Load(); tray != nil {
		s.TrayAvailable = tray.Available()
		s.TrayFailures = tray.Failures()
	}
	return s
}
func (h *Host) ReportFrontendReady(language string, screens int, contract string) error {
	if language != "ru" || screens != 5 || contract != "desktop-ui-v1" || h.engine.Load() == nil {
		return errors.New("контракт интерфейса не подтверждён")
	}
	h.readyOnce.Do(func() { close(h.frontendReady) })
	if h.smoke != nil {
		fmt.Fprintln(os.Stderr, "smoke: frontend contract confirmed")
	}
	return nil
}
func (h *Host) startup(ctx context.Context) {
	if h.smoke != nil {
		fmt.Fprintln(os.Stderr, "smoke: startup entered")
	}
	// Wails acquires single-instance ownership before invoking this callback.
	h.startupMu.Lock()
	defer h.startupMu.Unlock()
	h.mu.Lock()
	if h.closed {
		h.mu.Unlock()
		return
	}
	h.ctx = ctx
	h.mu.Unlock()
	settings, err := background.NewSettingsStore(filepath.Join(h.directory, "settings.json"))
	if err != nil {
		h.failStartup()
		return
	}
	var autostart background.Autostart
	var publisher background.Publisher = unavailablePublisher{}
	var probe resources.Probe
	var resolver resources.Resolver
	var native lifecycle.Runtime
	var credentials remote.CredentialStore
	if h.smoke != nil {
		autostart = &smokeAutostart{}
		probe = smokeProbe{}
		resolver = smokeResolver{}
	} else {
		autostart, err = background.NewWindowsAutostart(h.executable)
		if err != nil {
			h.failStartup()
			return
		}
		tray, createErr := background.NewWindowsTray(background.TrayCommands{Show: h.show, Toggle: func() {
			if e := h.engine.Load(); e != nil {
				e.ToggleNotifications()
			}
		}, Exit: h.exit}, func() bool { e := h.engine.Load(); return e != nil && e.Snapshot().NotificationsPaused })
		if createErr == nil {
			h.tray.Store(tray)
			publisher = tray
		}
		probe = resources.NewWindowsProbe()
		resolver = resources.WindowsResolver{}
		h.native = lifecycle.NewWindowsRuntime()
		native = h.native
		credentials, err = remote.NewFileStore(filepath.Join(h.directory, "remote-access.json"), remote.WindowsProtector{})
		if err != nil {
			h.failStartup()
			return
		}
	}
	e, err := desktop.Start(h.config, desktop.Dependencies{DataDirectory: h.directory, Settings: settings, Autostart: autostart, Publisher: publisher, Probe: probe, Resolver: resolver, Lifecycle: native, Credentials: credentials, OSVersion: winhost.OSVersion()})
	if err != nil {
		h.failStartup()
		return
	}
	h.engine.Store(e)
	if h.smoke != nil {
		fmt.Fprintln(os.Stderr, "smoke: engine initialized")
	}
	h.mu.Lock()
	h.message = "Локальный runtime готов"
	h.mu.Unlock()
	// Background startup cannot leave an inaccessible process if tray creation fails.
	if h.smoke == nil && (h.tray.Load() == nil || !h.tray.Load().Available()) {
		h.show(false)
	}
	if h.smoke != nil {
		go h.verifySmoke()
	}
}
func (h *Host) failStartup() {
	h.mu.Lock()
	h.message = "Не удалось подготовить локальный runtime. Исходные файлы не заменялись."
	h.mu.Unlock()
	if h.smoke != nil {
		h.exit()
	} else {
		h.show(false)
	}
}
func (h *Host) domReady(context.Context) {
	h.domLoaded.Store(true)
	if h.visible.Load() {
		h.show(h.showSettings.Load())
	}
}
func (h *Host) show(settings bool) {
	h.visible.Store(true)
	h.showSettings.Store(settings)
	ctx := h.context()
	if ctx == nil || !h.domLoaded.Load() {
		return
	}
	wailsRuntime.WindowShow(ctx)
	wailsRuntime.WindowUnminimise(ctx)
	wailsRuntime.EventsEmit(ctx, "inspector:visibility", true)
	if settings {
		wailsRuntime.EventsEmit(ctx, "inspector:navigate", "settings")
	}
}
func (h *Host) hide() {
	tray := h.tray.Load()
	if tray == nil || !tray.Available() {
		return
	}
	ctx := h.context()
	if ctx == nil {
		return
	}
	h.visible.Store(false)
	wailsRuntime.EventsEmit(ctx, "inspector:visibility", false)
	wailsRuntime.WindowHide(ctx)
}
func (h *Host) exit() {
	h.exitRequested.Store(true)
	if ctx := h.context(); ctx != nil {
		wailsRuntime.Quit(ctx)
	}
}
func (h *Host) beforeClose(context.Context) bool {
	if h.exitRequested.Load() {
		return false
	}
	tray := h.tray.Load()
	if tray != nil && tray.Available() {
		h.hide()
		return true
	}
	return false
}
func (h *Host) shutdown(context.Context) {
	h.startupMu.Lock()
	defer h.startupMu.Unlock()
	h.mu.Lock()
	h.closed = true
	h.mu.Unlock()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	if e := h.engine.Load(); e != nil {
		if err := e.Close(ctx); err != nil {
			h.smokePassed.Store(false)
		}
	}
	if tray := h.tray.Load(); tray != nil {
		if err := tray.Close(ctx); err != nil {
			h.smokePassed.Store(false)
		}
	}
	if h.native != nil {
		h.native.Close()
	} // Release ownership handles, never stop backends on app exit.
}

type unavailablePublisher struct{}

func (unavailablePublisher) Publish(background.Notification) error {
	return errors.New("Windows tray недоступен")
}
