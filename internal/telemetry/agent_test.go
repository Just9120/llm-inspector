package telemetry

import (
	"encoding/json"
	"strings"
	"testing"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestRequestToolCountsWithoutPrivateContent(t *testing.T) {
	s := NewRequestSession()
	body := `{"tools":[{"function":{"name":"a","parameters":{"content":"PRIVATE_ARGS"}}},{}],"messages":[{"role":"tool","content":"OLD_RESULT"},{"role":"user","content":"PRIVATE_PROMPT"},{"role":"tool","content":"PRIVATE_RESULT1"},{"content":"PRIVATE_RESULT2","role":"tool"}]}`
	for _, b := range []byte(body) {
		s.Observe([]byte{b})
	}
	available, results := s.Complete(true)
	if available.Value == nil || *available.Value != 2 || results == nil || *results != 2 {
		t.Fatal("incorrect available/trailing tool counts")
	}
	for _, body := range []string{`{`, `[]`, `{"tools":[],"tools":[{}]}`} {
		s := NewRequestSession()
		s.Observe([]byte(body))
		m, n := s.Complete(true)
		if m.Value != nil || n != nil {
			t.Fatal("ambiguous request credited")
		}
	}
	s = NewRequestSession()
	s.Observe([]byte(`{}`))
	m, n := s.Complete(false)
	if m.Value != nil || n != nil {
		t.Fatal("partial body credited")
	}
}

func TestLargeRequestContentNeverGetsDecoded(t *testing.T) {
	s := NewRequestSession()
	s.Observe([]byte(`{"messages":[{"role":"user","content":"`))
	for i := 0; i < 200; i++ {
		s.Observe([]byte(strings.Repeat("PRIVATE", 1024)))
	}
	s.Observe([]byte(`"},{"role":"tool","content":"PRIVATE_RESULT"}],"tools":[{}]}`))
	m, n := s.Complete(true)
	if m.Value == nil || *m.Value != 1 || n == nil || *n != 1 {
		t.Fatal("large content prevented bounded role projection")
	}
}

func TestStreamingToolsAssembleNamesNotArguments(t *testing.T) {
	data := []string{
		`{"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"name":"read_","arguments":"PRIVATE_ARGS"}}]}}]}`,
		`{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"list_files"}},{"index":1,"function":{"name":"file","arguments":"PRIVATE_CONTINUATION"}}]}}]}`,
		`{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}`, `[DONE]`,
	}
	for _, chunk := range []int{1, 7, 4096} {
		s := NewSession(domain.Ollama, "text/event-stream")
		body := []byte("data: " + strings.Join(data, "\n\ndata: ") + "\n\n")
		for i := 0; i < len(body); i += chunk {
			s.Observe(body[i:min(i+chunk, len(body))])
		}
		got := s.AgentResponse()
		if got.InvokedTools.Value == nil || *got.InvokedTools.Value != 2 || !got.DetailsComplete || got.Completion != "tool_calls" || got.Tools[0].Name != "list_files" || got.Tools[1].Name != "read_file" {
			t.Fatalf("tool fragment mismatch: %+v", got)
		}
		b, _ := json.Marshal(got)
		if strings.Contains(string(b), "PRIVATE") {
			t.Fatal("arguments escaped")
		}
	}
}

func TestAgentAmbiguousOrTruncatedResponseFailsClosed(t *testing.T) {
	for _, body := range []string{
		"data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"name\":\"one\"}}]}}]}\n\n",
		"data: {broken}\n\ndata: {\"choices\":[{\"finish_reason\":\"stop\"}]}\n\n",
		"data: {\"choices\":[{\"finish_reason\":\"stop\"},{\"finish_reason\":\"stop\"}]}\n\n",
		"data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":99999,\"function\":{\"name\":\"one\"}}]},\"finish_reason\":\"tool_calls\"}]}\n\n",
	} {
		s := NewSession(domain.Ollama, "text/event-stream")
		s.Observe([]byte(body))
		if s.AgentResponse().InvokedTools.Value != nil {
			t.Fatal("ambiguous agent response credited")
		}
	}
	s := NewSession(domain.Ollama, "application/json")
	s.Observe([]byte(`{"choices":[{"message":{"tool_calls":[{"function":{"name":"PRIVATE PROSE"}}]},"finish_reason":"tool_calls"}]}`))
	got := s.AgentResponse()
	if got.InvokedTools.Value == nil || *got.InvokedTools.Value != 1 || got.DetailsComplete || len(got.Tools) != 0 {
		t.Fatal("invalid tool name escaped")
	}
}

func TestContextOverflowUsesOnlyValidAllowlistedCodes(t *testing.T) {
	for _, tc := range []struct {
		body     string
		expected bool
	}{
		{`{"error":{"code":"context_length_exceeded","message":"PRIVATE_PROMPT"}}`, true},
		{`{"error":{"type":"context_overflow"}}`, true},
		{`{"error":{"message":"context_length_exceeded"}}`, false},
		{`{"error":{"code":"context_length_exceeded"},}`, false},
	} {
		s := NewErrorSession()
		s.Observe([]byte(tc.body))
		if s.ContextOverflow() != tc.expected {
			t.Fatal("error text used as evidence")
		}
	}
}
