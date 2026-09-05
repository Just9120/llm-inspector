package gateway

import (
	"bytes"
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func startTestGateway(t *testing.T, backend *httptest.Server, sink chan domain.Observation) string {
	t.Helper()
	g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL, Port: 0}, sink, true)
	if err != nil {
		t.Fatal(err)
	}
	address, err := g.Start()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		_ = g.Stop(ctx)
	})
	return address
}

func TestProxyBytesHeadersAttributionAndPrivacy(t *testing.T) {
	request := `{"messages":[{"role":"user","content":"FORBIDDEN_PROMPT"}],"tools":[{"type":"function","function":{"name":"test","parameters":{"secret":"FORBIDDEN_ARGS"}}}]}`
	response := "data: {\"model\":\"fixture-model\",\"choices\":[{\"delta\":{\"content\":\"FORBIDDEN_RESPONSE\"}}]}\n\ndata: {\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":8}}\n\ndata: [DONE]\n\n"
	var mu sync.Mutex
	var received []byte
	var gotHeaders http.Header
	var gotPath string
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		mu.Lock()
		received = body
		gotHeaders = r.Header.Clone()
		gotPath = r.URL.RequestURI()
		mu.Unlock()
		w.Header().Set("Content-Type", "text/event-stream")
		w.Header().Set("X-Test", "preserved")
		for _, b := range []byte(response) {
			_, _ = w.Write([]byte{b})
			w.(http.Flusher).Flush()
		}
	}))
	defer backend.Close()
	sink := make(chan domain.Observation, 2)
	base := startTestGateway(t, backend, sink)
	req, _ := http.NewRequest(http.MethodPost, base+"/clients/opencode/v1/chat/completions?key=preserved", strings.NewReader(request))
	req.Header.Set("Authorization", "Bearer LOCAL_BACKEND_AUTH")
	req.Header.Set("X-LLM-Inspector-Session-Id", strings.Repeat("1", 32))
	req.Header.Set("X-LLM-Inspector-Turn-Id", strings.Repeat("2", 32))
	req.Header.Set("X-LLM-Inspector-Turn-Sequence", "1")
	req.Header.Set("Connection", "X-Hop")
	req.Header.Set("X-Hop", "not-forwarded")
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	body, err := io.ReadAll(resp.Body)
	resp.Body.Close()
	if err != nil || string(body) != response || resp.Header.Get("X-Test") != "preserved" {
		t.Fatal("response changed")
	}
	mu.Lock()
	defer mu.Unlock()
	if !bytes.Equal(received, []byte(request)) || gotPath != "/v1/chat/completions?key=preserved" || gotHeaders.Get("Authorization") != "Bearer LOCAL_BACKEND_AUTH" || gotHeaders.Get("X-Hop") != "" || gotHeaders.Get("X-LLM-Inspector-Session-Id") != "" {
		t.Fatal("request boundary mismatch")
	}
	select {
	case obs := <-sink:
		if obs.Client != domain.OpenCode || obs.Correlation == nil || obs.Telemetry.TotalTokens.Value == nil || *obs.Telemetry.TotalTokens.Value != 13 || obs.TTFT.Value == nil {
			t.Fatal("telemetry missing")
		}
		data, _ := json.Marshal(obs)
		for _, secret := range []string{"FORBIDDEN_PROMPT", "FORBIDDEN_RESPONSE", "FORBIDDEN_ARGS", "LOCAL_BACKEND_AUTH", "key=preserved"} {
			if bytes.Contains(data, []byte(secret)) {
				t.Fatal("private data escaped projection")
			}
		}
	case <-time.After(time.Second):
		t.Fatal("observation missing")
	}
}

func TestNoRedirectAndDefaultRemoteDenied(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) { http.Redirect(w, r, "http://192.0.2.1/", 302) }))
	defer backend.Close()
	base := startTestGateway(t, backend, make(chan domain.Observation, 1))
	client := &http.Client{CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
	resp, err := client.Get(base + "/v1/models")
	if err != nil {
		t.Fatal(err)
	}
	resp.Body.Close()
	if resp.StatusCode != 302 {
		t.Fatal("redirect followed")
	}
	for _, host := range []string{"evil.example", "node.tailnet.ts.net"} {
		req, _ := http.NewRequest("GET", base+"/v1/models", nil)
		req.Host = host
		resp, err := client.Do(req)
		if err != nil {
			t.Fatal(err)
		}
		resp.Body.Close()
		if resp.StatusCode != 403 {
			t.Fatal("remote accepted without opt-in")
		}
	}
}

func TestClientCancellationReachesBackendAndFullSinkDoesNotBlock(t *testing.T) {
	started := make(chan struct{})
	cancelled := make(chan struct{})
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = io.Copy(io.Discard, r.Body)
		close(started)
		select {
		case <-r.Context().Done():
			close(cancelled)
		case <-time.After(5 * time.Second):
		}
	}))
	defer backend.Close()
	sink := make(chan domain.Observation)
	base := startTestGateway(t, backend, sink)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	req, _ := http.NewRequestWithContext(ctx, "POST", base+"/v1/chat/completions", strings.NewReader(`{}`))
	done := make(chan struct{})
	go func() {
		resp, _ := http.DefaultClient.Do(req)
		if resp != nil {
			resp.Body.Close()
		}
		close(done)
	}()
	select {
	case <-started:
	case <-time.After(3 * time.Second):
		t.Fatal("backend not started")
	}
	cancel()
	select {
	case <-cancelled:
	case <-time.After(3 * time.Second):
		t.Fatal("cancellation not propagated")
	}
	select {
	case <-done:
	case <-time.After(3 * time.Second):
		t.Fatal("request blocked")
	}
}

func TestConfigurationFailsClosed(t *testing.T) {
	for _, target := range []string{"http://0.0.0.0:1234/", "http://example.com/", "http://127.0.0.1@evil.example/", "http://127.0.0.1/path", "http://127.0.0.1/?secret=TOKEN", "http://127.0.0.1/#", "file:///C:/test", "https://127.0.0.1:0/"} {
		c := DefaultConfig(domain.Ollama)
		c.BackendURL = target
		if err := c.Validate(); err == nil || strings.Contains(err.Error(), target) {
			t.Fatal("invalid target accepted or disclosed")
		}
	}
	for _, args := range [][]string{{"--listener-port=0"}, {"--backend=unknown"}, {"--backend-url=http://127.0.0.1", "--remote-backend-url=https://node.tailnet.ts.net"}, {"--secret=TOKEN"}, {"--background", "--background"}} {
		if _, _, err := ParseLaunch(args); err == nil {
			t.Fatal("invalid options accepted")
		}
	}
	c := DefaultConfig(domain.Ollama)
	c.BackendURL = "http://localhost:11434/"
	u, err := c.target(false)
	if err != nil || u.Host != "127.0.0.1:11434" {
		t.Fatal("localhost not normalized")
	}
}
