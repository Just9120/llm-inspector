package telemetry

import (
	"encoding/json"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestExistingBackendFixturesEveryChunkBoundary(t *testing.T) {
	root := "../../tests/LlmInspector.ContractTests/Fixtures/epic02/v1"
	for _, backend := range []domain.Backend{domain.Ollama, domain.LlamaCpp, domain.LMStudio} {
		for _, kind := range []string{"nonstreaming.json", "streaming.sse"} {
			name := string(backend) + "-" + kind
			data, err := os.ReadFile(filepath.Join(root, name))
			if err != nil {
				t.Fatal(err)
			}
			media := "application/json"
			if strings.HasSuffix(kind, "sse") {
				media = "text/event-stream"
			}
			var baseline string
			for _, chunk := range []int{1, 2, 3, 7, 31, 256, 4096} {
				s := NewSession(backend, media)
				for i := 0; i < len(data); i += chunk {
					s.Observe(data[i:min(i+chunk, len(data))])
				}
				result := s.Complete()
				encoded, _ := json.Marshal(result)
				if result.PromptTokens.Value == nil || result.CompletionTokens.Value == nil || result.Model == "" {
					t.Fatalf("%s chunk %d: missing fixture metrics: %s", name, chunk, encoded)
				}
				if baseline == "" {
					baseline = string(encoded)
				} else if baseline != string(encoded) {
					t.Fatalf("%s depends on chunk size %d", name, chunk)
				}
				if strings.Contains(string(encoded), "synthetic-fixture") || strings.Contains(string(encoded), "arguments") {
					t.Fatal("content leak")
				}
			}
		}
	}
}

func TestUsageDetailsAndProvenance(t *testing.T) {
	data, err := os.ReadFile("../../tests/LlmInspector.ContractTests/Fixtures/openai-chat/v2/usage-details.json")
	if err != nil {
		t.Fatal(err)
	}
	s := NewSession(domain.Ollama, "application/json")
	s.Observe(data)
	result := s.Complete()
	if result.CachedTokens.Value == nil || *result.CachedTokens.Value != 80 || result.ReasoningTokens.Value == nil || *result.ReasoningTokens.Value != 12 {
		t.Fatal("missing token details")
	}
	encoded, _ := json.Marshal(result)
	if strings.Contains(string(encoded), "FORBIDDEN_REASONING") {
		t.Fatal("reasoning persisted")
	}
	if s.HasOutput() {
		t.Fatal("non-streaming TTFT fabricated")
	}
	if result.QueueTime.Value != nil || result.PromptSpeed.Value != nil {
		t.Fatal("unsupported native fields fabricated")
	}
}

func TestMalformedOrAmbiguousJSONFailsClosed(t *testing.T) {
	for _, body := range []string{
		`{"usage":{"prompt_tokens":12,}}`,
		`{"usage":{"prompt_tokens":12},"usage":{"prompt_tokens":99}}`,
		`{"usage":{"prompt_tokens":12}`,
		`{"usage":{"prompt_tokens":01}}`,
		`{"usage":{"prompt_tokens":12}} {}`,
		`{"usage/prompt_tokens":12}`,
		`{"usage":{"prompt_tokens":9007199254740993}}`,
		strings.Repeat("[", 65) + strings.Repeat("]", 65),
	} {
		s := NewSession(domain.Ollama, "application/json")
		s.Observe([]byte(body))
		if s.Complete().PromptTokens.Value != nil {
			t.Fatalf("accepted malformed/ambiguous %s", body)
		}
	}
}

// Fuzzing checks the finite projection boundary, not pass-through bytes (tested
// separately by the proxy). Seeds also run during ordinary go test.
func FuzzProjectionNeverPublishesInvalidMetrics(f *testing.F) {
	f.Add([]byte(`{"usage":{"prompt_tokens":4}}`), byte(1))
	f.Add([]byte(`{"usage":{"prompt_tokens":1e9999}}`), byte(7))
	f.Fuzz(func(t *testing.T, body []byte, size byte) {
		if len(body) > 65536 {
			t.Skip()
		}
		for _, media := range []string{"application/json", "text/event-stream"} {
			s := NewSession(domain.Ollama, media)
			chunk := int(size) + 1
			for i := 0; i < len(body); i += chunk {
				s.Observe(body[i:min(i+chunk, len(body))])
			}
			got := s.Complete()
			agent := s.AgentResponse()
			if len(agent.Tools) > MaxToolsPerTurn || agent.InvokedTools.Validate() != nil {
				t.Fatal("invalid agent metadata escaped parser")
			}
			for _, tool := range agent.Tools {
				if domain.TechnicalIdentifier(tool.Name) != tool.Name || tool.Sequence < 0 || tool.Sequence >= MaxToolsPerTurn {
					t.Fatal("invalid tool metadata escaped parser")
				}
			}
			for _, metric := range []domain.Metric{got.PromptTokens, got.CompletionTokens, got.TotalTokens, got.CachedTokens, got.ReasoningTokens, got.ContextUsage} {
				if err := metric.Validate(); err != nil {
					t.Fatal("invalid metric escaped parser")
				}
			}
		}
	})
}

func TestLargePrivateContentAndRecoveryAcrossSSEEvents(t *testing.T) {
	s := NewSession(domain.Ollama, "text/event-stream")
	s.Observe([]byte("data: {\"choices\":[{\"delta\":{\"content\":\""))
	chunk := []byte(strings.Repeat("PRIVATE_SENTINEL", 1000))
	for i := 0; i < 1000; i++ {
		s.Observe(chunk)
	}
	s.Observe([]byte("\"}}]}\n\ndata: {broken\n\ndata: {\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":3}}\n\ndata: [DONE]\n\n"))
	r := s.Complete()
	if r.TotalTokens.Value == nil || *r.TotalTokens.Value != 5 || r.TotalTokens.Quality != domain.Calculated || !s.HasOutput() {
		t.Fatal("large content prevented bounded extraction")
	}
	encoded, _ := json.Marshal(r)
	if strings.Contains(string(encoded), "PRIVATE_SENTINEL") {
		t.Fatal("content leak")
	}
	if s.parser.length > MaxTokenBytes {
		t.Fatal("unbounded token buffer")
	}
}

func TestTTFTExcludesReasoningAndToolOnlyEvents(t *testing.T) {
	for _, content := range []string{`{"choices":[{"delta":{"reasoning":"secret"}}]}`, `{"choices":[{"delta":{"tool_calls":[{"function":{"arguments":"secret"}}]}}]}`, `{"choices":[{"delta":{"content":""}}]}`} {
		s := NewSession(domain.Ollama, "text/event-stream")
		s.Observe([]byte("data: " + content + "\n\n"))
		s.Complete()
		if s.HasOutput() {
			t.Fatal("TTFT includes non-content event")
		}
	}
}
