package domain

import "time"

type Backend string
type Client string
type Stage string

const (
	Ollama           Backend = "ollama"
	LlamaCpp         Backend = "llama-cpp"
	LMStudio         Backend = "lm-studio"
	Generic          Client  = "generic"
	OpenCode         Client  = "opencode"
	Hermes           Client  = "hermes"
	Cline            Client  = "cline"
	OpenWebUI        Client  = "open-webui"
	ModelLoading     Stage   = "model_loading"
	QueueWaiting     Stage   = "queue_waiting"
	PromptProcessing Stage   = "prompt_processing"
	Generating       Stage   = "reasoning_generation"
	ToolWait         Stage   = "tool_wait"
	Completed        Stage   = "completed"
	Cancelled        Stage   = "cancelled"
	Failed           Stage   = "error"
)

type Correlation struct {
	SessionID   string `json:"session_id"`
	TurnID      string `json:"turn_id"`
	Sequence    int    `json:"sequence"`
	OperationID string `json:"operation_id,omitempty"`
}

type Telemetry struct {
	Backend          Backend           `json:"backend"`
	Model            string            `json:"model,omitempty"`
	PromptTokens     Metric            `json:"prompt_tokens"`
	CompletionTokens Metric            `json:"completion_tokens"`
	TotalTokens      Metric            `json:"total_tokens"`
	CachedTokens     Metric            `json:"cached_tokens"`
	ReasoningTokens  Metric            `json:"reasoning_tokens"`
	ContextUsage     Metric            `json:"context_usage"`
	ContextLimit     Metric            `json:"context_limit"`
	ContextHistory   Metric            `json:"context_history"`
	ContextTools     Metric            `json:"context_tools"`
	PromptSpeed      Metric            `json:"prompt_speed"`
	GenerationSpeed  Metric            `json:"generation_speed"`
	ModelLoadTime    Metric            `json:"model_load_time"`
	QueueTime        Metric            `json:"queue_time"`
	ModelLoad        string            `json:"model_load"`
	BackendMetrics   map[string]Metric `json:"backend_metrics"`
}

func MissingTelemetry(backend Backend) Telemetry {
	const v = "backend-telemetry-v1"
	t := func() Metric { return Missing(Tokens, "openai_usage", v) }
	s := func() Metric { return Missing(TokensPerSecond, "backend_extension", v) }
	return Telemetry{Backend: backend, PromptTokens: t(), CompletionTokens: t(), TotalTokens: t(), CachedTokens: t(), ReasoningTokens: t(), ContextUsage: t(), ContextLimit: t(), ContextHistory: t(), ContextTools: t(), PromptSpeed: s(), GenerationSpeed: s(), ModelLoadTime: Missing(Milliseconds, "backend_extension", v), QueueTime: Missing(Milliseconds, "backend_extension", v), ModelLoad: "unavailable", BackendMetrics: map[string]Metric{}}
}

type ToolCall struct {
	Sequence int    `json:"sequence"`
	Name     string `json:"name"`
}

type AgentTurn struct {
	AvailableTools  Metric     `json:"available_tools"`
	InvokedTools    Metric     `json:"invoked_tools"`
	ToolResults     *int       `json:"tool_results"`
	Tools           []ToolCall `json:"tools"`
	DetailsComplete bool       `json:"details_complete"`
	Completion      string     `json:"completion"`
}

func MissingAgentTurn() AgentTurn {
	return AgentTurn{AvailableTools: Missing(Count, "inspector", "agent-v1"), InvokedTools: Missing(Count, "inspector", "agent-v1"), Tools: []ToolCall{}, Completion: "unavailable"}
}

type Observation struct {
	RequestID     string        `json:"request_id"`
	StartedAt     time.Time     `json:"started_at"`
	DurationMS    float64       `json:"duration_ms"`
	HTTPStatus    *int          `json:"http_status"`
	Outcome       string        `json:"outcome"`
	ErrorType     string        `json:"error_type"`
	ErrorOrigin   string        `json:"error_origin"`
	Client        Client        `json:"client"`
	Telemetry     Telemetry     `json:"telemetry"`
	TTFT          Metric        `json:"ttft"`
	Correlation   *Correlation  `json:"correlation,omitempty"`
	ContextChange Metric        `json:"context_change"`
	Agent         AgentTurn     `json:"agent"`
	Runtime       *RuntimeFacts `json:"runtime,omitempty"`
	// A consumer persists the graph separately; do not recursively duplicate it
	// in every request JSON/export record.
	Operation *OperationGraph `json:"-"`
}
