package history

import (
	"encoding/hex"
	"math"
	"slices"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

// These numeric mappings are the durable C# v1-v5 SQLite representation.
var backends = []string{"ollama", "llama-cpp", "lm-studio"}
var clients = []string{"generic", "opencode", "hermes", "cline", "open-webui"}
var qualities = []string{"exact", "calculated", "estimated", "unavailable"}
var units = []string{"tokens", "token_delta", "nanoseconds", "milliseconds", "tokens_per_second", "percent", "count", "bytes", "celsius", "watts"}
var sources = []string{"openai_usage", "backend_extension", "inspector", "windows_api", "nvidia_smi", "gateway_traffic"}
var outcomes = []string{"completed", "backend_unavailable", "client_cancelled", "relay_failed"}
var errorsList = []string{"none", "backend_unavailable", "client_cancelled", "relay_failed", "connection_refused", "model_loading", "http_api_error", "timeout", "context_overflow", "backend_crash"}
var origins = []string{"not_applicable", "unknown", "inspector", "client", "backend", "model"}
var loads = []string{"unavailable", "cold", "warm"}
var stages = []string{"model_loading", "queue_waiting", "prompt_processing", "reasoning_generation", "tool_wait", "completed", "cancelled", "error"}
var stageEvidence = []string{"protocol_observed", "backend_reported"}
var operationStatus = []string{"running", "completed", "cancelled", "error"}
var toolStatus = []string{"started", "completed", "error"}

// ProxyErrorType and HistoryErrorType had distinct .NET enums. Preserve their
// reviewed mapping instead of rejecting cancellation/relay observations.
func errorCode(value string) int {
	switch value {
	case "client_cancellation":
		return 2
	case "relay_failure", "inspector_failure":
		return 3
	}
	return code(errorsList, value)
}

func code[T ~string](values []string, v T) int { return slices.Index(values, string(v)) }
func decode(values []string, n int) (string, error) {
	if n < 0 || n >= len(values) {
		return "", ErrInvalid
	}
	return values[n], nil
}
func id(s string) string {
	if len(s) == 36 {
		if s[8] != '-' || s[13] != '-' || s[18] != '-' || s[23] != '-' {
			return ""
		}
		s = strings.ReplaceAll(s, "-", "")
	}
	if len(s) != 32 || s == strings.Repeat("0", 32) {
		return ""
	}
	if _, err := hex.DecodeString(s); err != nil {
		return ""
	}
	return strings.ToLower(s)
}
func validOptionalID(s string) bool         { return s == "" || id(s) != "" }
func validOptionalIdentifier(s string) bool { return s == "" || domain.TechnicalIdentifier(s) != "" }
func finiteDuration(v float64) bool {
	return v >= 0 && v < float64(math.MaxInt64)/1e6 && !math.IsInf(v, 0) && !math.IsNaN(v)
}

func metricArgs(m domain.Metric, unit domain.Unit) ([]any, error) {
	if m.Unit != unit || m.Validate() != nil || code(sources, m.Source) < 0 || domain.TechnicalIdentifier(m.SourceVersion) == "" || !validOptionalIdentifier(m.DerivationVersion) {
		return nil, ErrInvalid
	}
	return []any{m.Value, code(units, m.Unit), code(qualities, m.Quality), code(sources, m.Source), m.SourceVersion, nullable(m.DerivationVersion)}, nil
}

func decodeMetric(v *float64, unit, quality, source int, version, derivation string) (domain.Metric, error) {
	u, e1 := decode(units, unit)
	q, e2 := decode(qualities, quality)
	s, e3 := decode(sources, source)
	m := domain.Metric{Value: v, Unit: domain.Unit(u), Quality: domain.Quality(q), Source: s, SourceVersion: version, DerivationVersion: derivation}
	if e1 != nil || e2 != nil || e3 != nil {
		return domain.Metric{}, ErrInvalid
	}
	if _, err := metricArgs(m, m.Unit); err != nil {
		return domain.Metric{}, err
	}
	return m, nil
}

type metricField struct {
	key   string
	unit  domain.Unit
	value *domain.Metric
}

func requestFields(o *domain.Observation) []metricField {
	t := &o.Telemetry
	return []metricField{
		{"input_tokens", domain.Tokens, &t.PromptTokens}, {"output_tokens", domain.Tokens, &t.CompletionTokens}, {"total_tokens", domain.Tokens, &t.TotalTokens},
		{"cached_tokens", domain.Tokens, &t.CachedTokens}, {"reasoning_tokens", domain.Tokens, &t.ReasoningTokens}, {"context_usage_tokens", domain.Tokens, &t.ContextUsage},
		{"context_limit_tokens", domain.Tokens, &t.ContextLimit}, {"context_history_tokens", domain.Tokens, &t.ContextHistory}, {"context_tool_tokens", domain.Tokens, &t.ContextTools},
		{"prompt_tokens_per_second", domain.TokensPerSecond, &t.PromptSpeed}, {"generation_tokens_per_second", domain.TokensPerSecond, &t.GenerationSpeed},
		{"ttft_ms", domain.Milliseconds, &o.TTFT}, {"model_load_ms", domain.Milliseconds, &t.ModelLoadTime}, {"queue_ms", domain.Milliseconds, &t.QueueTime},
	}
}
func resourceFields(r *domain.ResourceSample) []metricField {
	return []metricField{
		{"system_cpu_percent", domain.Percent, &r.CPU}, {"system_memory_percent", domain.Percent, &r.MemoryPercent}, {"system_memory_used_bytes", domain.Bytes, &r.MemoryUsed},
		{"process_cpu_percent", domain.Percent, &r.ProcessCPU}, {"process_memory_bytes", domain.Bytes, &r.ProcessMemory}, {"disk_read_bytes", domain.Bytes, &r.DiskRead}, {"disk_write_bytes", domain.Bytes, &r.DiskWrite},
		{"client_to_backend_bytes", domain.Bytes, &r.ClientToBackend}, {"backend_to_client_bytes", domain.Bytes, &r.BackendToClient}, {"gpu_utilization_percent", domain.Percent, &r.GPUUtilization},
		{"gpu_vram_used_bytes", domain.Bytes, &r.GPUVRAMUsed}, {"gpu_vram_total_bytes", domain.Bytes, &r.GPUVRAMTotal}, {"gpu_temperature_celsius", domain.Celsius, &r.GPUTemperature}, {"gpu_power_watts", domain.Watts, &r.GPUPower},
	}
}
