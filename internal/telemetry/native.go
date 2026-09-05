package telemetry

import (
	"math"
	"strconv"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func (s *Session) nativeScalar(v scalar) {
	if v.kind == 's' {
		switch v.path {
		case "/type":
			switch v.text {
			case "chat.end", "model_load.start", "model_load.end", "message.delta":
				s.current.eventType = v.text
			}
		case "/model_instance_id", "/result/model_instance_id":
			s.current.model = domain.TechnicalIdentifier(v.text)
		case "/content":
			s.current.output = v.nonempty
		}
		return
	}
	if v.kind != 'n' {
		return
	}
	path := strings.TrimPrefix(v.path, "/result")
	key := map[string]string{
		"/stats/input_tokens": "prompt", "/stats/total_output_tokens": "completion", "/stats/reasoning_output_tokens": "reasoning",
		"/stats/tokens_per_second": "generation_speed", "/stats/model_load_time_seconds": "load", "/load_time_seconds": "load",
	}[path]
	if key == "" {
		return
	}
	n, err := strconv.ParseFloat(v.text, 64)
	if err != nil || math.IsNaN(n) || math.IsInf(n, 0) || n < 0 {
		return
	}
	if (key == "prompt" || key == "completion" || key == "reasoning") && math.Trunc(n) != n {
		return
	}
	s.current.values[key] = n
}

func (s *Session) mergeNative() {
	if s.current.model != "" {
		s.accepted.model = s.current.model
	}
	if s.current.eventType == "model_load.start" {
		s.modelLoadStarted = true
	}
	if s.current.eventType == "message.delta" && s.current.output {
		s.accepted.output = true
	}
	if v, ok := s.current.values["load"]; ok {
		s.accepted.values["load"] = v
	}
	if (!s.sse || s.current.eventType == "chat.end") && s.current.stats {
		s.terminalStats = true
		for _, key := range []string{"prompt", "completion", "reasoning", "generation_speed"} {
			delete(s.accepted.values, key)
			if v, ok := s.current.values[key]; ok {
				s.accepted.values[key] = v
			}
		}
	}
}

func (s *Session) nativeTelemetry() domain.Telemetry {
	t := domain.MissingTelemetry(domain.LMStudio)
	if !s.terminalStats {
		return t
	}
	t.Model = s.accepted.model
	get := func(key string, unit domain.Unit) domain.Metric {
		if v, ok := s.accepted.values[key]; ok {
			return domain.Measured(v, unit, "backend_extension", "lm-studio-native-v1")
		}
		return domain.Missing(unit, "backend_extension", "lm-studio-native-v1")
	}
	t.PromptTokens = get("prompt", domain.Tokens)
	t.CompletionTokens = get("completion", domain.Tokens)
	t.ReasoningTokens = get("reasoning", domain.Tokens)
	if t.ReasoningTokens.Value != nil && t.CompletionTokens.Value != nil && *t.ReasoningTokens.Value > *t.CompletionTokens.Value {
		t.ReasoningTokens = domain.Missing(domain.Tokens, "backend_extension", "lm-studio-native-v1")
	}
	if t.PromptTokens.Value != nil && t.CompletionTokens.Value != nil {
		t.TotalTokens = domain.Derived(*t.PromptTokens.Value+*t.CompletionTokens.Value, domain.Tokens, domain.Calculated, "lm-studio-native-v1", "sum-input-output-v1")
	}
	t.ContextUsage = t.PromptTokens
	t.GenerationSpeed = get("generation_speed", domain.TokensPerSecond)
	if seconds, ok := s.accepted.values["load"]; ok {
		t.ModelLoadTime = domain.Measured(seconds*1000, domain.Milliseconds, "backend_extension", "lm-studio-native-v1")
		if t.ModelLoadTime.Value != nil {
			t.ModelLoad = "cold"
		}
	} else if !s.modelLoadStarted {
		t.ModelLoad = "warm"
		t.ModelLoadTime = domain.Measured(0, domain.Milliseconds, "backend_extension", "lm-studio-native-v1")
	}
	return t
}
