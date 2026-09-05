package gateway

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/resources"
)

func TestResourcesFollowRelayBytesAndTerminalObservation(t *testing.T) {
	const request = `{"messages":[{"role":"user","content":"PRIVATE_CONTENT"}]}`
	const response = "data: {\"choices\":[{\"delta\":{\"content\":\"PRIVATE_RESPONSE\"}}]}\n\ndata: [DONE]\n\n"
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		if string(body) != request {
			t.Error("request changed")
		}
		w.Header().Set("Content-Type", "text/event-stream")
		io.WriteString(w, response)
	}))
	defer backend.Close()
	observations := make(chan domain.Observation, 1)
	rows := make(chan []domain.ResourceSample, 1)
	m := resources.NewMonitor(nil, nil, func(s []domain.ResourceSample) {
		if len(observations) != 1 {
			t.Error("resources preceded final observation")
		}
		rows <- s
	})
	g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL, Port: 0}, observations, true)
	if err != nil {
		t.Fatal(err)
	}
	if err := g.SetResourceMonitor(m); err != nil {
		t.Fatal(err)
	}
	address, err := g.Start()
	if err != nil {
		t.Fatal(err)
	}
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		g.Stop(ctx)
		m.Close(ctx)
	}()
	if g.SetResourceMonitor(nil) == nil {
		t.Fatal("live reconfiguration accepted")
	}
	resp, err := http.Post(address+"/v1/chat/completions", "application/json", strings.NewReader(request))
	if err != nil {
		t.Fatal(err)
	}
	body, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if string(body) != response {
		t.Fatal("response changed")
	}
	select {
	case samples := <-rows:
		last := samples[len(samples)-1]
		obs := <-observations
		if last.RequestID != obs.RequestID || last.Stage.Stage != domain.Completed || *last.ClientToBackend.Value != float64(len(request)) || *last.BackendToClient.Value != float64(len(response)) {
			t.Fatal("resource counters/stage/identity mismatch")
		}
		if obs.TTFT.Quality != domain.Calculated || obs.TTFT.DerivationVersion != "first-output-monotonic-v1" {
			t.Fatal("TTFT provenance")
		}
	case <-time.After(time.Second):
		t.Fatal("missing resource completion")
	}
}

type brokenMonitor struct{ startPanic bool }

func (b brokenMonitor) Start(domain.RequestResourceContext) domain.ResourceSession {
	if b.startPanic {
		panic("start")
	}
	return brokenSession{}
}

type brokenSession struct{}

func (brokenSession) StageChanged(domain.StageValue) { panic("stage") }
func (brokenSession) AddSent(int)                    { panic("sent") }
func (brokenSession) AddReceived(int)                { panic("received") }
func (brokenSession) Complete()                      { panic("complete") }
func TestResourceFailuresDoNotChangeForwarding(t *testing.T) {
	for _, startPanic := range []bool{false, true} {
		backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { io.Copy(w, r.Body) }))
		g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL, Port: 0}, make(chan domain.Observation, 1), true)
		if err != nil {
			t.Fatal(err)
		}
		g.SetResourceMonitor(brokenMonitor{startPanic: startPanic})
		w := httptest.NewRecorder()
		r := httptest.NewRequest(http.MethodPost, "http://localhost/v1/chat/completions", strings.NewReader(`{"safe":true}`))
		r.RemoteAddr = "127.0.0.1:1234"
		g.ServeHTTP(w, r)
		if w.Code != 200 || w.Body.String() != `{"safe":true}` {
			t.Fatalf("collector affected forwarding: %d", w.Code)
		}
		backend.Close()
	}
}
