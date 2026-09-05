package resources_test

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/background"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/gateway"
	"github.com/Just9120/llm-inspector/internal/history"
	"github.com/Just9120/llm-inspector/internal/resources"
)

type readyProbe struct{ entered chan struct{} }

func (p readyProbe) Capture(context.Context, *domain.ProcessAssociation) (resources.Snapshot, error) {
	p.entered <- struct{}{}
	return resources.Snapshot{CapturedAt: time.Now(), MemoryAvailable: true, TotalMemory: 10000, AvailableMemory: 6000}, nil
}
func TestHiddenWindowPolicyKeepsProxyResourceHistoryPipelineAlive(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	store, err := history.Open(ctx, filepath.Join(t.TempDir(), "history.db"))
	if err != nil {
		t.Fatal(err)
	}
	defer store.Close()
	buffer := history.NewBuffered(store)
	probe := readyProbe{make(chan struct{}, 2)}
	monitor := resources.NewMonitor(probe, nil, func(samples []domain.ResourceSample) { buffer.OfferResourceTimeline(samples) })
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		select {
		case <-probe.entered:
		case <-r.Context().Done():
			return
		}
		io.Copy(io.Discard, r.Body)
		w.Header().Set("Content-Type", "application/json")
		io.WriteString(w, `{"choices":[{"message":{"content":"PRIVATE_RESPONSE"},"finish_reason":"stop"}],"usage":{"prompt_tokens":3,"completion_tokens":4}}`)
	}))
	defer backend.Close()
	g, err := gateway.New(gateway.Config{Backend: domain.Ollama, BackendURL: backend.URL, Port: 5117}, buffer.Observations())
	if err != nil {
		t.Fatal(err)
	}
	if err = g.SetResourceMonitor(monitor); err != nil {
		t.Fatal(err)
	}
	proxy := httptest.NewServer(g)
	defer proxy.Close()
	lifetime := background.Lifetime{BackgroundAvailable: true}
	for i := 0; i < 2; i++ {
		if lifetime.OnClosing() != background.HideAndContinue {
			t.Fatal("window close stops process")
		}
		req, _ := http.NewRequestWithContext(ctx, http.MethodPost, proxy.URL+"/v1/chat/completions", strings.NewReader(`{"messages":[{"role":"user","content":"PRIVATE_PROMPT"}]}`))
		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			t.Fatal(err)
		}
		io.Copy(io.Discard, resp.Body)
		resp.Body.Close()
	}
	proxy.Close()
	if err = monitor.Close(ctx); err != nil {
		t.Fatal(err)
	}
	if err = buffer.Close(ctx); err != nil {
		t.Fatal(err)
	}
	if h := buffer.Health(); h.Failed != 0 || h.Dropped != 0 {
		t.Fatal(h)
	}
	slice, err := store.Slice(ctx, history.Filter{})
	if err != nil {
		t.Fatal(err)
	}
	if len(slice.Requests) != 2 || len(slice.Resources) < 4 {
		t.Fatal("history did not continue", len(slice.Requests), len(slice.Resources))
	}
	ids := map[string]bool{}
	for _, r := range slice.Requests {
		ids[r.Observation.RequestID] = true
	}
	for _, r := range slice.Resources {
		if !ids[r.RequestID] {
			t.Fatal("resource FK correlation lost")
		}
	}
	bytes, err := json.Marshal(slice)
	if err != nil || strings.Contains(string(bytes), "PRIVATE_") {
		t.Fatal("content escaped technical pipeline", err)
	}
}
