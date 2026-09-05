package telemetry

import (
	"encoding/json"
	"os"
	"strings"
	"testing"
)

func TestNativeFixturesEveryChunkBoundary(t *testing.T) {
	for _, tc := range []struct {
		name                     string
		prompt, completion, load float64
		disposition              string
	}{
		{"cold-nonstreaming.json", 646, 586, 2656, "cold"}, {"warm-nonstreaming.json", 700, 40, 0, "warm"},
		{"cold-streaming.sse", 329, 268, 3250, "cold"}, {"warm-streaming.sse", 350, 20, 0, "warm"},
	} {
		t.Run(tc.name, func(t *testing.T) {
			data, err := os.ReadFile("../../tests/LlmInspector.ContractTests/Fixtures/epic04/lm-studio-native-v1/" + tc.name)
			if err != nil {
				t.Fatal(err)
			}
			for _, chunk := range []int{1, 2, 7, 256, 4096} {
				media := "application/json"
				streaming := strings.HasSuffix(tc.name, ".sse")
				if streaming {
					media = "text/event-stream"
				}
				s := NewNativeSession(media)
				for i := 0; i < len(data); i += chunk {
					s.Observe(data[i:min(i+chunk, len(data))])
				}
				got := s.Complete()
				if got.PromptTokens.Value == nil || *got.PromptTokens.Value != tc.prompt || got.CompletionTokens.Value == nil || *got.CompletionTokens.Value != tc.completion || got.ModelLoadTime.Value == nil || *got.ModelLoadTime.Value != tc.load || got.ModelLoad != tc.disposition || s.HasOutput() != streaming {
					t.Fatalf("native fixture mismatch, chunk=%d: %+v", chunk, got)
				}
				b, _ := json.Marshal(got)
				if strings.Contains(string(b), "FORBIDDEN") || strings.Contains(string(b), "resp_synthetic") {
					t.Fatal("content escaped")
				}
			}
		})
	}
}

func TestNativeRequiresValidTerminalStats(t *testing.T) {
	for _, body := range []string{
		"data: {\"type\":\"model_load.start\"}\n\n",
		"data: {\"type\":\"message.delta\",\"stats\":{\"input_tokens\":10}}\n\n",
		"data: {\"type\":\"chat.end\",\"stats\":{\"input_tokens\":10},}\n\n",
	} {
		s := NewNativeSession("text/event-stream")
		s.Observe([]byte(body))
		got := s.Complete()
		if got.PromptTokens.Value != nil || got.ModelLoadTime.Value != nil || got.ModelLoad != "unavailable" {
			t.Fatal("fabricated terminal stats")
		}
	}
	s := NewNativeSession("text/event-stream")
	s.Observe([]byte("data: {\"type\":\"model_load.start\"}\n\ndata: {\"type\":\"chat.end\",\"stats\":{\"input_tokens\":10}}\n\n"))
	if got := s.Complete(); got.PromptTokens.Value == nil || got.ModelLoadTime.Value != nil || got.ModelLoad != "unavailable" {
		t.Fatal("loading start without end is not warm")
	}
}

func TestSSELineEndingsAndMultilineData(t *testing.T) {
	for _, newline := range []string{"\n", "\r\n", "\r"} {
		s := NewNativeSession("text/event-stream")
		body := "event: chat.end\ndata: {\"type\":\"chat.end\",\ndata: \"stats\":{\"input_tokens\":9}}\n\n"
		for _, b := range []byte(strings.ReplaceAll(body, "\n", newline)) {
			s.Observe([]byte{b})
		}
		if got := s.Complete(); got.PromptTokens.Value == nil || *got.PromptTokens.Value != 9 {
			t.Fatal("line ending changed semantics")
		}
	}
}
