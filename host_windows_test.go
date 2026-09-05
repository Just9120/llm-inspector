//go:build windows

package main

import (
	"context"
	"reflect"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
)

func TestHostBindsOnlyShellContract(t *testing.T) {
	typ := reflect.TypeFor[*Host]()
	want := []string{"GetShellState", "ReportFrontendReady"}
	if typ.NumMethod() != len(want) {
		t.Fatal("unreviewed host binding surface", typ.NumMethod())
	}
	for i, name := range want {
		if typ.Method(i).Name != name {
			t.Fatal(typ.Method(i).Name)
		}
	}
	h := newHost(gateway.DefaultConfig(domain.Ollama), t.TempDir(), "unused.exe", nil)
	if h.GetShellState().Ready || h.context() != nil || h.ReportFrontendReady("ru", 5, "desktop-ui-v1") == nil {
		t.Fatal("uninitialized host accepted")
	}
	if h.beforeClose(context.Background()) {
		t.Fatal("no tray must exit, not hide")
	}
	h.shutdown(context.Background())
	h.startup(context.Background())
	if h.GetShellState().Ready {
		t.Fatal("closed host restarted")
	}
}

func TestIsolatedSmokeNeverTouchesAutostartAndHasValidUnavailableProbe(t *testing.T) {
	a := &smokeAutostart{}
	if enabled, err := a.IsEnabled(); enabled || err != nil {
		t.Fatal(enabled, err)
	}
	if a.SetEnabled(true) == nil {
		t.Fatal("autostart allowed in smoke")
	}
	s, err := (smokeProbe{}).Capture(context.Background(), nil)
	if err != nil || s.CapturedAt.IsZero() || s.CapturedAt.After(time.Now()) || s.CPUAvailable || s.MemoryAvailable {
		t.Fatal(s, err)
	}
}
