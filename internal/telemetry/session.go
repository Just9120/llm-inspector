package telemetry

import (
	"math"
	"strconv"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type projection struct {
	model     string
	values    map[string]float64
	output    bool
	eventType string
	stats     bool
}

// Session holds only a fixed set of numeric metrics and a validated model name.
// SSE lines are fed incrementally; no line or document body is accumulated.
type Session struct {
	backend          domain.Backend
	sse              bool
	parser           jsonProjection
	current          projection
	accepted         projection
	prefix           [5]byte
	lineLength       int
	dataLine         bool
	eventData        bool
	skipSpace        bool
	closed           bool
	native           bool
	terminalStats    bool
	modelLoadStarted bool
	previousCR       bool
}

var numericPaths = map[string]string{
	"/usage/prompt_tokens":                              "prompt",
	"/usage/completion_tokens":                          "completion",
	"/usage/total_tokens":                               "total",
	"/usage/prompt_tokens_details/cached_tokens":        "cached",
	"/usage/completion_tokens_details/reasoning_tokens": "reasoning",
	"/timings/cache_n":                                  "cache_n",
	"/timings/prompt_n":                                 "prompt_n",
	"/timings/predicted_n":                              "predicted_n",
	"/timings/prompt_ms":                                "prompt_ms",
	"/timings/predicted_ms":                             "predicted_ms",
	"/timings/prompt_per_second":                        "prompt_per_second",
	"/timings/predicted_per_second":                     "predicted_per_second",
}

func NewSession(backend domain.Backend, contentType string) *Session {
	s := &Session{backend: backend, sse: strings.HasPrefix(strings.ToLower(contentType), "text/event-stream"), accepted: projection{values: map[string]float64{}}}
	s.resetParser()
	return s
}

// NewNativeSession is intentionally distinct from OpenAI-compatible LM Studio.
// Only native terminal stats prove warm/cold timing; missing fields stay missing.
func NewNativeSession(contentType string) *Session {
	s := NewSession(domain.LMStudio, contentType)
	s.native = true
	return s
}

func (s *Session) resetParser() {
	s.current = projection{values: map[string]float64{}}
	s.parser = jsonProjection{onScalar: s.scalar, allowText: func(path string) bool {
		return path == "/model" || (s.native && (path == "/type" || path == "/model_instance_id" || path == "/result/model_instance_id"))
	}, onObjectEnd: func(path string) {
		if path == "/stats" || path == "/result/stats" {
			s.current.stats = true
		}
	}}
}

func (s *Session) scalar(v scalar) {
	if s.native {
		s.nativeScalar(v)
		return
	}
	if v.path == "/model" && v.kind == 's' {
		s.current.model = domain.TechnicalIdentifier(v.text)
	}
	if v.path == "/choices/0/delta/content" && v.kind == 's' && v.nonempty && s.sse {
		s.current.output = true
	}
	if v.kind != 'n' {
		return
	}
	key, ok := numericPaths[v.path]
	if !ok {
		return
	}
	if strings.HasPrefix(v.path, "/timings/") && s.backend != domain.LlamaCpp {
		return
	}
	n, err := strconv.ParseFloat(v.text, 64)
	if err != nil || math.IsNaN(n) || math.IsInf(n, 0) || n < 0 {
		return
	}
	if (strings.HasPrefix(v.path, "/usage/") || strings.HasSuffix(v.path, "_n")) && math.Trunc(n) != n {
		return
	}
	s.current.values[key] = n
}

func (s *Session) merge() {
	if s.parser.complete() {
		if s.native {
			s.mergeNative()
			s.resetParser()
			return
		}
		for k, v := range s.current.values {
			s.accepted.values[k] = v
		}
		if s.current.model != "" {
			s.accepted.model = s.current.model
		}
		s.accepted.output = s.accepted.output || s.current.output
	}
	s.resetParser()
}

func (s *Session) Observe(data []byte) {
	if s.closed {
		return
	}
	if !s.sse {
		s.parser.feed(data)
		return
	}
	for _, b := range data {
		if b == '\n' && s.previousCR {
			s.previousCR = false
			continue
		}
		s.previousCR = b == '\r'
		if b == '\r' {
			b = '\n'
		}
		if b == '\n' {
			if s.lineLength == 0 {
				if s.eventData {
					s.merge()
					s.eventData = false
				}
			} else if s.dataLine {
				s.parser.byte('\n')
				s.eventData = true
			}
			s.lineLength = 0
			s.dataLine = false
			s.skipSpace = false
			continue
		}
		if s.lineLength < 5 {
			s.prefix[s.lineLength] = b
			s.lineLength++
			if s.lineLength == 5 {
				s.dataLine = string(s.prefix[:]) == "data:"
				s.skipSpace = s.dataLine
			}
			continue
		}
		// Count saturates; an unbounded content line cannot overflow state.
		if s.lineLength < 6 {
			s.lineLength++
		}
		if !s.dataLine {
			continue
		}
		if s.skipSpace {
			s.skipSpace = false
			if b == ' ' {
				continue
			}
		}
		s.parser.byte(b)
	}
}

func (s *Session) HasOutput() bool {
	return s.sse && (s.accepted.output || (!s.native && !s.parser.invalid && s.current.output))
}

func (s *Session) Complete() domain.Telemetry {
	if !s.closed {
		if !s.sse || s.eventData || s.dataLine {
			s.merge()
		}
		s.closed = true
	}
	if s.native {
		return s.nativeTelemetry()
	}
	t := domain.MissingTelemetry(s.backend)
	t.Model = s.accepted.model
	get := func(key string, unit domain.Unit, source string) domain.Metric {
		if v, ok := s.accepted.values[key]; ok {
			return domain.Measured(v, unit, source, "backend-telemetry-v1")
		}
		return domain.Missing(unit, source, "backend-telemetry-v1")
	}
	t.PromptTokens = get("prompt", domain.Tokens, "openai_usage")
	t.CompletionTokens = get("completion", domain.Tokens, "openai_usage")
	t.TotalTokens = get("total", domain.Tokens, "openai_usage")
	if t.TotalTokens.Value == nil && t.PromptTokens.Value != nil && t.CompletionTokens.Value != nil {
		t.TotalTokens = domain.Derived(*t.PromptTokens.Value+*t.CompletionTokens.Value, domain.Tokens, domain.Calculated, "backend-telemetry-v1", "sum-prompt-completion-v1")
	}
	t.CachedTokens = get("cached", domain.Tokens, "openai_usage")
	t.ReasoningTokens = get("reasoning", domain.Tokens, "openai_usage")
	t.ContextUsage = t.PromptTokens
	if t.CachedTokens.Value != nil && t.PromptTokens.Value != nil && *t.CachedTokens.Value > *t.PromptTokens.Value {
		t.CachedTokens = domain.Missing(domain.Tokens, "openai_usage", "backend-telemetry-v1")
	}
	if t.ReasoningTokens.Value != nil && t.CompletionTokens.Value != nil && *t.ReasoningTokens.Value > *t.CompletionTokens.Value {
		t.ReasoningTokens = domain.Missing(domain.Tokens, "openai_usage", "backend-telemetry-v1")
	}
	if s.backend == domain.LlamaCpp {
		for key, unit := range map[string]domain.Unit{"cache_n": domain.Tokens, "prompt_n": domain.Tokens, "predicted_n": domain.Tokens, "prompt_ms": domain.Milliseconds, "predicted_ms": domain.Milliseconds, "prompt_per_second": domain.TokensPerSecond, "predicted_per_second": domain.TokensPerSecond} {
			t.BackendMetrics[key] = get(key, unit, "backend_extension")
		}
		t.PromptSpeed = t.BackendMetrics["prompt_per_second"]
		t.GenerationSpeed = t.BackendMetrics["predicted_per_second"]
		if t.CachedTokens.Value == nil {
			t.CachedTokens = t.BackendMetrics["cache_n"]
		}
	}
	return t
}
