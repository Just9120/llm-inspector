//go:build windows

package main

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"strings"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/resources"
)

const smokeCanary = "synthetic-smoke-private-content-never-store"

type smokeFixture struct {
	directory string
	server    *httptest.Server
	config    gateway.Config
}

func newSmokeFixture() (*smokeFixture, error) {
	directory, err := os.MkdirTemp("", "llm-inspector-desktop-smoke-")
	if err != nil {
		return nil, err
	}
	f := &smokeFixture{directory: directory}
	f.server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		fmt.Fprintf(w, `{"model":"smoke-model","choices":[{"message":{"content":"%s"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":5,"total_tokens":8}}`, smokeCanary)
	}))
	listener, err := net.Listen("tcp4", "127.0.0.1:0")
	if err != nil {
		f.close()
		return nil, err
	}
	f.config = gateway.Config{Backend: domain.Ollama, BackendURL: f.server.URL, Port: listener.Addr().(*net.TCPAddr).Port}
	listener.Close()
	return f, nil
}
func (f *smokeFixture) close() {
	if f.server != nil {
		f.server.Close()
	}
	// This exact directory is exclusively created by MkdirTemp above, never an
	// environment-supplied or user-provided deletion target. WebView2 may exit late.
	for i := 0; i < 20; i++ {
		if os.RemoveAll(f.directory) == nil {
			return
		}
		time.Sleep(100 * time.Millisecond)
	}
}
func (h *Host) verifySmoke() {
	defer h.exit()
	ctx, cancel := context.WithTimeout(context.Background(), 45*time.Second)
	defer cancel()
	e := h.engine.Load()
	fmt.Fprintln(os.Stderr, "smoke: relay validation entered")
	if e == nil || !e.Snapshot().Status.ProxyRunning || e.History == nil {
		return
	}
	request, err := http.NewRequestWithContext(ctx, http.MethodPost, e.Snapshot().Status.Listener+"/clients/hermes/v1/chat/completions", strings.NewReader(`{"model":"smoke-model","messages":[{"role":"user","content":"`+smokeCanary+`"}]}`))
	if err != nil {
		return
	}
	request.Header.Set("Content-Type", "application/json")
	client := &http.Client{Timeout: 10 * time.Second, Transport: &http.Transport{Proxy: nil}}
	defer client.CloseIdleConnections()
	response, err := client.Do(request)
	if err != nil {
		return
	}
	body, err := io.ReadAll(io.LimitReader(response.Body, 8192))
	response.Body.Close()
	if err != nil || response.StatusCode != 200 || !strings.Contains(string(body), smokeCanary) {
		return
	}
	ticker := time.NewTicker(20 * time.Millisecond)
	defer ticker.Stop()
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			state := e.Snapshot()
			if state.Latest == nil || state.Writer.Written < 2 {
				continue
			}
			encoded, err := json.Marshal(state)
			if err != nil || strings.Contains(string(encoded), smokeCanary) {
				return
			}
			rows, err := e.History.Query(ctx, history.Filter{})
			if err != nil || len(rows.Items) != 1 || rows.Items[0].Telemetry.TotalTokens.Value == nil || *rows.Items[0].Telemetry.TotalTokens.Value != 8 {
				return
			}
			encoded, err = json.Marshal(rows)
			if err != nil || strings.Contains(string(encoded), smokeCanary) {
				return
			}
			select {
			case <-h.frontendReady:
			case <-ctx.Done():
				fmt.Fprintln(os.Stderr, "smoke: frontend contract timeout")
				return
			}
			h.smokePassed.Store(true)
			return
		}
	}
}

type smokeAutostart struct{}

func (*smokeAutostart) IsEnabled() (bool, error) { return false, nil }
func (*smokeAutostart) SetEnabled(bool) error {
	return errors.New("autostart changes disabled in isolated smoke")
}

type smokeProbe struct{}

func (smokeProbe) Capture(context.Context, *domain.ProcessAssociation) (resources.Snapshot, error) {
	return resources.Snapshot{CapturedAt: time.Now()}, nil
}

type smokeResolver struct{}

func (smokeResolver) Resolve(string) *domain.ProcessAssociation { return nil }
