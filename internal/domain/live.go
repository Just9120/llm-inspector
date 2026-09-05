package domain

import "time"

type StageValue struct {
	Stage         Stage  `json:"stage"`
	Evidence      string `json:"evidence"`
	SourceVersion string `json:"source_version"`
}

func (s StageValue) Valid() bool {
	if TechnicalIdentifier(s.SourceVersion) == "" || (s.Evidence != "protocol_observed" && s.Evidence != "backend_reported") {
		return false
	}
	switch s.Stage {
	case ModelLoading, QueueWaiting, PromptProcessing, Generating, ToolWait, Completed, Cancelled, Failed:
		return true
	}
	return false
}
func (s StageValue) Terminal() bool {
	return s.Stage == Completed || s.Stage == Cancelled || s.Stage == Failed
}

type LiveRequest struct {
	RequestID string     `json:"request_id"`
	Client    Client     `json:"client"`
	StartedAt time.Time  `json:"started_at"`
	Stage     StageValue `json:"stage"`
	Elapsed   Metric     `json:"elapsed"`
	Progress  Metric     `json:"progress"`
	ETA       Metric     `json:"eta"`
}

type LiveSnapshot struct {
	Active         []LiveRequest `json:"active"`
	LatestTerminal *LiveRequest  `json:"latest_terminal"`
	Omitted        uint64        `json:"omitted"`
}

func (m Metric) Clone() Metric {
	if m.Value != nil {
		v := *m.Value
		m.Value = &v
	}
	return m
}
