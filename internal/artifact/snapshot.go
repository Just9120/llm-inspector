// Package artifact builds offline, previewable exports from closed technical DTOs.
// It deliberately does not serialize history/domain records wholesale.
package artifact

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"runtime"
	"sort"
	"strings"
	"time"
	"unicode"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/history"
)

type Fact struct {
	Availability  string  `json:"availability"`
	Value         *string `json:"value"`
	SourceVersion string  `json:"source_version"`
}

func fact(value, source string) Fact {
	if value == "" {
		return Fact{Availability: "unavailable", SourceVersion: source}
	}
	return Fact{Availability: "available", Value: &value, SourceVersion: source}
}
func (f Fact) valid() bool {
	if len(f.SourceVersion) == 0 || len(f.SourceVersion) > 128 {
		return false
	}
	for _, c := range f.SourceVersion {
		if c > 127 || !unicode.IsLetter(c) && !unicode.IsDigit(c) && !strings.ContainsRune("._-", c) {
			return false
		}
	}
	if f.Availability == "unavailable" {
		return f.Value == nil
	}
	if f.Availability != "available" || f.Value == nil || strings.TrimSpace(*f.Value) == "" || len(*f.Value) > 256 {
		return false
	}
	for _, c := range *f.Value {
		if unicode.IsControl(c) {
			return false
		}
	}
	return true
}

type Environment struct {
	OS          Fact `json:"operating_system_version"`
	GPUDriver   Fact `json:"gpu_driver_version"`
	Backend     Fact `json:"backend_version"`
	Client      Fact `json:"client_version"`
	Application Fact `json:"application_version"`
	Framework   Fact `json:"framework_version"`
}

// EnvironmentFromVersions accepts only technical identifiers from trusted probes.
// No hostname, username, path, command line or environment variable is collected.
func EnvironmentFromVersions(os, driver, backend, client string) Environment {
	return Environment{OS: fact(domain.TechnicalIdentifier(os), "windows-version-v1"), GPUDriver: fact(domain.TechnicalIdentifier(driver), "nvidia-smi-version-v1"), Backend: fact(domain.TechnicalIdentifier(backend), "backend-version-v1"), Client: fact(domain.TechnicalIdentifier(client), "client-version-v1"), Application: fact("1.0.0", "local-runtime-facts-v1"), Framework: fact(runtime.Version(), "local-runtime-facts-v1")}
}

type Selection struct {
	Scope       string     `json:"scope"`
	From        *time.Time `json:"from_utc"`
	To          *time.Time `json:"to_utc"`
	OperationID *string    `json:"operation_id"`
}

func TimeRange(from, to time.Time) Selection {
	f, t := from.UTC(), to.UTC()
	return Selection{Scope: "time_range", From: &f, To: &t}
}
func Operation(operation string) Selection {
	return Selection{Scope: "operation", OperationID: &operation}
}
func (s Selection) filter() (history.Filter, error) {
	switch s.Scope {
	case "time_range":
		if s.From != nil && s.To != nil && s.OperationID == nil && !s.From.After(*s.To) {
			return history.Filter{From: s.From, To: s.To}, nil
		}
	case "operation":
		if s.From == nil && s.To == nil && s.OperationID != nil && guid(*s.OperationID) != "" {
			return history.Filter{OperationID: *s.OperationID}, nil
		}
	}
	return history.Filter{}, history.ErrInvalid
}

type Metric struct {
	Key           string         `json:"key"`
	Value         *float64       `json:"value"`
	Unit          string         `json:"unit"`
	Quality       domain.Quality `json:"quality"`
	Source        string         `json:"source"`
	SourceVersion string         `json:"source_version"`
	Derivation    *string        `json:"derivation_version"`
}
type Request struct {
	ID          string    `json:"request_id"`
	OperationID *string   `json:"operation_id"`
	StartedAt   time.Time `json:"started_at_utc"`
	HTTPStatus  *int      `json:"http_status_code"`
	Outcome     string    `json:"outcome"`
	ErrorType   string    `json:"error_type"`
	Client      string    `json:"client"`
	Backend     string    `json:"backend"`
	Model       Fact      `json:"model"`
	ModelLoad   string    `json:"model_load_disposition"`
	Metrics     []Metric  `json:"runtime_metrics"`
}
type Resource struct {
	ID            string    `json:"sample_id"`
	RequestID     *string   `json:"request_id"`
	OperationID   *string   `json:"operation_id"`
	CapturedAt    time.Time `json:"captured_at_utc"`
	Stage         string    `json:"stage"`
	StageEvidence string    `json:"stage_evidence"`
	GPUDeviceID   *string   `json:"gpu_device_id"`
	Dropped       int       `json:"dropped_sample_count"`
	Metrics       []Metric  `json:"system_metrics"`
}
type Truncation struct {
	Requests  bool `json:"requests_truncated"`
	Resources bool `json:"resource_samples_truncated"`
}
type Snapshot struct {
	Schema      string      `json:"schema_version"`
	GeneratedAt time.Time   `json:"generated_at_utc"`
	Selection   Selection   `json:"selection"`
	Environment Environment `json:"environment"`
	Requests    []Request   `json:"requests"`
	Resources   []Resource  `json:"resource_samples"`
	Truncation  Truncation  `json:"truncation"`
}
type Artifact struct {
	JSON   string `json:"json"`
	SHA256 string `json:"sha256"`
	data   []byte
}

func encode(document any) (Artifact, error) {
	b, err := json.MarshalIndent(document, "", "  ")
	if err != nil {
		return Artifact{}, err
	}
	h := sha256.Sum256(b)
	return Artifact{JSON: string(b), SHA256: hex.EncodeToString(h[:]), data: b}, nil
}

func CreateSnapshot(ctx context.Context, store *history.Store, selection Selection, environment Environment, now time.Time) (Artifact, error) {
	for _, f := range []Fact{environment.OS, environment.GPUDriver, environment.Backend, environment.Client, environment.Application, environment.Framework} {
		if !f.valid() {
			return Artifact{}, history.ErrInvalid
		}
	}
	filter, err := selection.filter()
	if err != nil {
		return Artifact{}, err
	}
	slice, err := store.Slice(ctx, filter)
	if err != nil {
		return Artifact{}, err
	}
	requests, resources, err := project(slice)
	if err != nil {
		return Artifact{}, err
	}
	if selection.OperationID != nil {
		g := guid(*selection.OperationID)
		selection.OperationID = &g
	}
	return encode(Snapshot{Schema: "diagnostic-snapshot-v1", GeneratedAt: now.UTC(), Selection: selection, Environment: environment, Requests: requests, Resources: resources, Truncation: Truncation{Requests: slice.RequestsTruncated, Resources: slice.ResourcesTruncated}})
}

var requestKeys = map[string]string{"input_tokens": "InputTokens", "output_tokens": "OutputTokens", "total_tokens": "TotalTokens", "cached_tokens": "CachedTokens", "reasoning_tokens": "ReasoningTokens", "context_usage_tokens": "ContextUsageTokens", "context_limit_tokens": "ContextLimitTokens", "context_history_tokens": "ContextHistoryTokens", "context_tool_tokens": "ContextToolTokens", "prompt_tokens_per_second": "PromptTokensPerSecond", "generation_tokens_per_second": "GenerationTokensPerSecond", "ttft_ms": "TimeToFirstTokenMilliseconds", "model_load_ms": "ModelLoadMilliseconds", "queue_ms": "QueueMilliseconds", "total_duration_ms": "TotalDurationMilliseconds"}

func project(slice history.Slice) ([]Request, []Resource, error) {
	requests := []Request{}
	resources := []Resource{}
	for _, r := range slice.Requests {
		client := map[domain.Client]string{domain.Generic: "generic_unknown", domain.OpenCode: "open_code_desktop", domain.Hermes: "hermes_desktop", domain.Cline: "cline", domain.OpenWebUI: "open_web_ui"}[r.Client]
		backend := map[domain.Backend]string{domain.Ollama: "ollama", domain.LlamaCpp: "llama_cpp", domain.LMStudio: "lm_studio"}[r.Telemetry.Backend]
		if client == "" || backend == "" || guid(r.RequestID) == "" || r.Telemetry.Model != "" && domain.TechnicalIdentifier(r.Telemetry.Model) == "" {
			return nil, nil, history.ErrInvalid
		}
		entry := Request{ID: guid(r.RequestID), OperationID: ptr(guid(r.OperationID)), StartedAt: r.StartedAt.UTC(), HTTPStatus: r.HTTPStatus, Outcome: r.Outcome, ErrorType: r.ErrorType, Client: client, Backend: backend, Model: fact(r.Telemetry.Model, "history-model-identifier-v1"), ModelLoad: r.Telemetry.ModelLoad, Metrics: []Metric{}}
		for key, m := range r.Metrics {
			target, ok := requestKeys[key]
			if !ok {
				return nil, nil, history.ErrInvalid
			}
			metric, err := projectMetric(target, m)
			if err != nil {
				return nil, nil, err
			}
			entry.Metrics = append(entry.Metrics, metric)
		}
		sort.Slice(entry.Metrics, func(i, j int) bool { return entry.Metrics[i].Key < entry.Metrics[j].Key })
		requests = append(requests, entry)
	}
	for _, r := range slice.Resources {
		entry := Resource{ID: guid(r.ID), RequestID: ptr(guid(r.RequestID)), OperationID: ptr(guid(r.OperationID)), CapturedAt: r.CapturedAt.UTC(), Stage: "unavailable", StageEvidence: "unavailable", GPUDeviceID: ptr(r.GPUDeviceID), Dropped: r.DroppedSamples, Metrics: []Metric{}}
		if r.Stage != nil {
			entry.Stage = map[domain.Stage]string{domain.ModelLoading: "ModelLoading", domain.QueueWaiting: "QueueWaiting", domain.PromptProcessing: "PromptProcessing", domain.Generating: "ReasoningGeneration", domain.ToolWait: "ToolWait", domain.Completed: "Completed", domain.Cancelled: "Cancelled", domain.Failed: "Error"}[r.Stage.Stage]
			entry.StageEvidence = map[string]string{"protocol_observed": "ProtocolObserved", "backend_reported": "BackendReported"}[r.Stage.Evidence]
			if entry.Stage == "" || entry.StageEvidence == "" {
				return nil, nil, history.ErrInvalid
			}
		}
		metrics := map[string]domain.Metric{"cpu_percent": r.CPU, "memory_percent": r.MemoryPercent, "memory_used_bytes": r.MemoryUsed, "process_cpu_percent": r.ProcessCPU, "process_memory_bytes": r.ProcessMemory, "disk_read_bytes": r.DiskRead, "disk_write_bytes": r.DiskWrite, "client_to_backend_bytes": r.ClientToBackend, "backend_to_client_bytes": r.BackendToClient, "gpu_utilization_percent": r.GPUUtilization, "gpu_vram_used_bytes": r.GPUVRAMUsed, "gpu_vram_total_bytes": r.GPUVRAMTotal, "gpu_temperature_celsius": r.GPUTemperature, "gpu_power_watts": r.GPUPower}
		for key, m := range metrics {
			metric, err := projectMetric(key, m)
			if err != nil {
				return nil, nil, err
			}
			entry.Metrics = append(entry.Metrics, metric)
		}
		sort.Slice(entry.Metrics, func(i, j int) bool { return entry.Metrics[i].Key < entry.Metrics[j].Key })
		resources = append(resources, entry)
	}
	sort.Slice(requests, func(i, j int) bool {
		if requests[i].StartedAt.Equal(requests[j].StartedAt) {
			return requests[i].ID < requests[j].ID
		}
		return requests[i].StartedAt.Before(requests[j].StartedAt)
	})
	sort.Slice(resources, func(i, j int) bool {
		if resources[i].CapturedAt.Equal(resources[j].CapturedAt) {
			return resources[i].ID < resources[j].ID
		}
		return resources[i].CapturedAt.Before(resources[j].CapturedAt)
	})
	return requests, resources, nil
}
func projectMetric(key string, m domain.Metric) (Metric, error) {
	if m.Validate() != nil || domain.TechnicalIdentifier(m.SourceVersion) == "" || m.DerivationVersion != "" && domain.TechnicalIdentifier(m.DerivationVersion) == "" {
		return Metric{}, history.ErrInvalid
	}
	source := m.Source
	if source == "openai_usage" {
		source = "open_ai_usage"
	}
	switch source {
	case "open_ai_usage", "backend_extension", "inspector", "windows_api", "nvidia_smi", "gateway_traffic":
	default:
		return Metric{}, history.ErrInvalid
	}
	unit := string(m.Unit)
	if m.Unit == domain.Tokens {
		unit = "token_count"
	}
	return Metric{Key: key, Value: m.Value, Unit: unit, Quality: m.Quality, Source: source, SourceVersion: m.SourceVersion, Derivation: ptr(m.DerivationVersion)}, nil
}
func ptr(s string) *string {
	if s == "" {
		return nil
	}
	return &s
}
func guid(s string) string {
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
	s = strings.ToLower(s)
	return s[:8] + "-" + s[8:12] + "-" + s[12:16] + "-" + s[16:20] + "-" + s[20:]
}

var ErrArtifact = errors.New("сохранение требует неизменённого локального preview")
