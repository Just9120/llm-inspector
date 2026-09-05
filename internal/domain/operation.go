package domain

import "time"

type TurnRecord struct {
	TurnID         string    `json:"turn_id"`
	RequestID      string    `json:"request_id"`
	Sequence       int       `json:"sequence"`
	StartedAt      time.Time `json:"started_at"`
	DurationMS     float64   `json:"duration_ms"`
	Outcome        string    `json:"outcome"`
	ErrorType      string    `json:"error_type"`
	AvailableTools Metric    `json:"available_tools"`
	InvokedTools   Metric    `json:"invoked_tools"`
}

type ToolEvent struct {
	ID           string    `json:"id"`
	TurnSequence int       `json:"turn_sequence"`
	Sequence     int       `json:"sequence"`
	Name         string    `json:"name"`
	StartedAt    time.Time `json:"started_at"`
	Duration     Metric    `json:"duration"`
	Status       string    `json:"status"`
	ErrorType    string    `json:"error_type"`
}

type OperationGraph struct {
	ID        string       `json:"id"`
	SessionID string       `json:"session_id"`
	StartedAt time.Time    `json:"started_at"`
	EndedAt   *time.Time   `json:"ended_at"`
	Client    Client       `json:"client"`
	Backend   Backend      `json:"backend"`
	Model     string       `json:"model,omitempty"`
	Status    string       `json:"status"`
	ErrorType string       `json:"error_type"`
	Turns     []TurnRecord `json:"turns"`
	Tools     []ToolEvent  `json:"tools"`
	Truncated bool         `json:"truncated"`
}
