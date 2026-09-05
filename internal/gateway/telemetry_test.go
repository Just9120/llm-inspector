package gateway

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestGatewayBuildsToolOperationAndContextDelta(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = io.Copy(io.Discard, r.Body)
		w.Header().Set("Content-Type", "application/json")
		if r.Header.Get("X-Fixture-Turn") == "1" {
			_, _ = io.WriteString(w, `{"choices":[{"message":{"tool_calls":[{"function":{"name":"read_file","arguments":"PRIVATE_ARGS"}}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10}}`)
		} else {
			_, _ = io.WriteString(w, `{"choices":[{"message":{"content":"PRIVATE_RESPONSE"},"finish_reason":"stop"}],"usage":{"prompt_tokens":20}}`)
		}
	}))
	defer backend.Close()
	sink := make(chan domain.Observation, 2)
	g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL}, sink, true)
	if err != nil {
		t.Fatal(err)
	}
	base, err := g.Start()
	if err != nil {
		t.Fatal(err)
	}
	defer g.Stop(context.Background())
	for i := 1; i <= 2; i++ {
		body := `{"tools":[{}],"messages":[{"role":"user","content":"PRIVATE_PROMPT"}]}`
		if i == 2 {
			body = `{"tools":[{}],"messages":[{"role":"tool","content":"PRIVATE_RESULT"}]}`
		}
		req, _ := http.NewRequest("POST", base+"/clients/cline/v1/chat/completions", strings.NewReader(body))
		req.Header.Set("X-Fixture-Turn", strconv.Itoa(i))
		req.Header.Set(correlationHeaders[0], strings.Repeat("1", 32))
		req.Header.Set(correlationHeaders[1], strings.Repeat(strconv.Itoa(i+1), 32))
		req.Header.Set(correlationHeaders[2], strconv.Itoa(i))
		req.Header.Set(correlationHeaders[3], strings.Repeat("4", 32))
		resp, err := http.DefaultClient.Do(req)
		if err != nil {
			t.Fatal(err)
		}
		_, _ = io.Copy(io.Discard, resp.Body)
		resp.Body.Close()
		select {
		case got := <-sink:
			if got.Agent.AvailableTools.Value == nil || *got.Agent.AvailableTools.Value != 1 || got.Operation == nil {
				t.Fatal("agent projection missing")
			}
			if i == 2 && (got.ContextChange.Value == nil || *got.ContextChange.Value != 10 || got.Operation.Status != "completed" || got.Operation.Tools[0].Status != "completed") {
				t.Fatal("adjacent operation/context missing")
			}
			data, _ := json.Marshal(got)
			if strings.Contains(string(data), "PRIVATE") {
				t.Fatal("privacy boundary")
			}
		case <-time.After(time.Second):
			t.Fatal("no observation")
		}
	}
	if g.ActiveCount() != 0 || len(g.LiveSnapshot().Active) != 0 || g.LiveSnapshot().LatestTerminal.Stage.Stage != domain.Completed {
		t.Fatal("active state leak")
	}
}

func TestHTTPErrorClassificationAndContentFreeLiveFailure(t *testing.T) {
	for _, tc := range []struct {
		status             int
		body, kind, origin string
	}{
		{503, `{"error":{"message":"PRIVATE"}}`, "model_loading", "model"},
		{408, `{}`, "timeout", "backend"}, {504, `{}`, "timeout", "backend"},
		{413, `{}`, "context_overflow", "model"}, {400, `{"error":{"code":"context_length_exceeded","message":"PRIVATE"}}`, "context_overflow", "model"},
		{400, `{"error":{"message":"context_length_exceeded"}}`, "http_api_error", "backend"},
	} {
		t.Run(strconv.Itoa(tc.status)+tc.kind, func(t *testing.T) {
			backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				_, _ = io.Copy(io.Discard, r.Body)
				w.WriteHeader(tc.status)
				_, _ = io.WriteString(w, tc.body)
			}))
			defer backend.Close()
			sink := make(chan domain.Observation, 1)
			g, _ := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL}, sink, true)
			base, err := g.Start()
			if err != nil {
				t.Fatal(err)
			}
			defer g.Stop(context.Background())
			resp, err := http.Post(base+"/v1/chat/completions", "application/json", strings.NewReader(`{}`))
			if err != nil {
				t.Fatal(err)
			}
			body, _ := io.ReadAll(resp.Body)
			resp.Body.Close()
			if string(body) != tc.body || resp.StatusCode != tc.status {
				t.Fatal("error relay changed")
			}
			select {
			case got := <-sink:
				if got.ErrorType != tc.kind || got.ErrorOrigin != tc.origin || g.LiveSnapshot().LatestTerminal.Stage.Stage != domain.Failed {
					t.Fatal("error classification")
				}
			case <-time.After(time.Second):
				t.Fatal("missing observation")
			}
		})
	}
}
