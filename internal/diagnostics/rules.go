// Package diagnostics evaluates versioned rules using technical evidence only.
package diagnostics

import (
	"errors"
	"fmt"
	"math"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Conclusion struct {
	Rule        string     `json:"rule"`
	Kind        string     `json:"kind"`
	RuleVersion string     `json:"rule_version"`
	Explanation string     `json:"explanation"`
	Evidence    []Evidence `json:"evidence"`
}
type Activity struct {
	RequestID     string    `json:"request_id"`
	State         string    `json:"state"`
	ObservedAt    time.Time `json:"observed_at"`
	SourceVersion string    `json:"source_version"`
}
type Evidence struct {
	Kind      string             `json:"kind"`
	Metric    *domain.Metric     `json:"metric,omitempty"`
	Stage     *domain.StageValue `json:"stage,omitempty"`
	ErrorType string             `json:"error_type,omitempty"`
	Activity  *Activity          `json:"activity,omitempty"`
}
type Input struct {
	Latest     *domain.Observation
	Resource   *domain.ResourceSample
	Live       domain.LiveSnapshot
	Activities []Activity
	CapturedAt time.Time
}
type Options struct {
	Version                                                                                                                 string
	LargePrompt, SlowGeneration, OffloadCPU, OffloadGPU, VRAMPressure, ModelLoadMS, QueueMS, HighContext, StallAssessmentMS float64
}

func DefaultOptions() Options {
	return Options{"diagnostic-rules-v1", 8192, 10, 60, 20, 90, 1000, 1000, 90, 30000}
}

type Rules struct{ options Options }

func New(options Options) (*Rules, error) {
	bad := errors.New("некорректные диагностические пороги")
	if domain.TechnicalIdentifier(options.Version) == "" {
		return nil, bad
	}
	for _, v := range []float64{options.LargePrompt, options.SlowGeneration, options.OffloadCPU, options.VRAMPressure, options.ModelLoadMS, options.QueueMS, options.HighContext, options.StallAssessmentMS} {
		if v <= 0 || math.IsInf(v, 0) || math.IsNaN(v) {
			return nil, bad
		}
	}
	if options.OffloadCPU > 100 || options.VRAMPressure > 100 || options.HighContext > 100 || options.OffloadGPU < 0 || options.OffloadGPU > 100 || math.IsNaN(options.OffloadGPU) {
		return nil, bad
	}
	return &Rules{options}, nil
}
func Default() *Rules { r, _ := New(DefaultOptions()); return r }
func valid(m domain.Metric, unit domain.Unit) bool {
	return m.Unit == unit && m.Validate() == nil && m.Value != nil
}
func metricEvidence(name string, m domain.Metric) Evidence {
	m = m.Clone()
	return Evidence{Kind: name, Metric: &m}
}
func kind(m domain.Metric) string {
	if m.Quality == domain.Estimated {
		return "hypothesis"
	}
	return "fact"
}
func Ratio(numerator, denominator domain.Metric) domain.Metric {
	missing := domain.Missing(domain.Percent, "inspector", "diagnostic-rules-v1")
	if numerator.Unit != denominator.Unit || numerator.Validate() != nil || denominator.Validate() != nil || numerator.Value == nil || denominator.Value == nil || *denominator.Value <= 0 || *numerator.Value > *denominator.Value {
		return missing
	}
	quality := domain.Calculated
	if numerator.Quality == domain.Estimated || denominator.Quality == domain.Estimated {
		quality = domain.Estimated
	}
	return domain.Derived(*numerator.Value / *denominator.Value * 100, domain.Percent, quality, "diagnostic-rules-v1", "diagnostic-ratio-v1")
}

func (r *Rules) Evaluate(input Input) []Conclusion {
	result := []Conclusion{}
	add := func(rule, kind, explanation string, evidence ...Evidence) {
		if evidence == nil {
			evidence = []Evidence{}
		}
		result = append(result, Conclusion{rule, kind, r.options.Version, explanation, evidence})
	}
	insufficient := func(rule, explanation string) { add(rule, "insufficient_data", explanation) }
	threshold := func(rule, title, name string, m domain.Metric, unit domain.Unit, value float64, low bool) {
		if !valid(m, unit) {
			insufficient(rule, "Недостаточно данных: "+title+". Причина не утверждается.")
			return
		}
		if (!low && *m.Value >= value) || (low && *m.Value <= value) {
			explanation := fmt.Sprintf("%s: %.3f; порог %.3f. Правило описывает метрику, а не доказывает первопричину.", title, *m.Value, value)
			if m.Quality == domain.Estimated {
				explanation += " Исходная метрика — оценка, поэтому вывод является гипотезой."
			}
			add(rule, kind(m), explanation, metricEvidence(name, m))
		}
	}
	if o := input.Latest; o != nil {
		t := o.Telemetry
		threshold("large_prompt", "Большой входной контекст", "input_tokens", t.PromptTokens, domain.Tokens, r.options.LargePrompt, false)
		threshold("slow_generation", "Низкая скорость генерации", "generation_tokens_per_second", t.GenerationSpeed, domain.TokensPerSecond, r.options.SlowGeneration, true)
		resource := input.Resource
		if resource != nil && (resource.RequestID == "" || resource.RequestID != o.RequestID) {
			resource = nil
		}
		if resource == nil || !resource.Process.Valid() || !valid(resource.ProcessCPU, domain.Percent) || !valid(resource.GPUUtilization, domain.Percent) {
			insufficient("cpu_offload", "Нет подтверждённых CPU/GPU метрик и process association этого запроса; CPU offload не утверждается.")
		} else if *resource.ProcessCPU.Value >= r.options.OffloadCPU && *resource.GPUUtilization.Value <= r.options.OffloadGPU {
			add("cpu_offload", "hypothesis", "Высокая загрузка CPU backend и низкая загрузка GPU совместимы с CPU offload, но не доказывают размещение слоёв модели.", metricEvidence("process_cpu_percent", resource.ProcessCPU), metricEvidence("gpu_utilization_percent", resource.GPUUtilization))
		}
		vram := domain.Missing(domain.Percent, "inspector", r.options.Version)
		if resource != nil && valid(resource.GPUVRAMUsed, domain.Bytes) && valid(resource.GPUVRAMTotal, domain.Bytes) {
			vram = Ratio(resource.GPUVRAMUsed, resource.GPUVRAMTotal)
		}
		before := len(result)
		threshold("vram_pressure", "Высокое использование VRAM", "gpu_vram_usage_percent", vram, domain.Percent, r.options.VRAMPressure, false)
		if len(result) > before && result[len(result)-1].Kind != "insufficient_data" && resource != nil {
			result[len(result)-1].Evidence = append(result[len(result)-1].Evidence, metricEvidence("gpu_vram_used_bytes", resource.GPUVRAMUsed), metricEvidence("gpu_vram_total_bytes", resource.GPUVRAMTotal))
		}
		if t.ModelLoad == "unavailable" || t.ModelLoad == "" {
			insufficient("model_loading_latency", "Нет подтверждения cold/warm request; задержка загрузки модели не утверждается.")
		} else if t.ModelLoad == "cold" {
			if !valid(t.ModelLoadTime, domain.Milliseconds) {
				add("model_loading_latency", "hypothesis", "Backend сообщил cold start, но длительность загрузки неизвестна.", Evidence{Kind: "cold_model_load"})
			} else {
				threshold("model_loading_latency", "Задержка загрузки модели", "model_load_milliseconds", t.ModelLoadTime, domain.Milliseconds, r.options.ModelLoadMS, false)
			}
		}
		threshold("queue_waiting_latency", "Ожидание в очереди backend", "queue_milliseconds", t.QueueTime, domain.Milliseconds, r.options.QueueMS, false)
		context := domain.Missing(domain.Percent, "inspector", r.options.Version)
		if valid(t.ContextUsage, domain.Tokens) && valid(t.ContextLimit, domain.Tokens) {
			context = Ratio(t.ContextUsage, t.ContextLimit)
		}
		before = len(result)
		threshold("high_context_usage", "Высокое заполнение контекстного окна", "context_usage_percent", context, domain.Percent, r.options.HighContext, false)
		if len(result) > before && result[len(result)-1].Kind != "insufficient_data" {
			result[len(result)-1].Evidence = append(result[len(result)-1].Evidence, metricEvidence("context_usage_tokens", t.ContextUsage), metricEvidence("context_limit_tokens", t.ContextLimit))
		}
		if o.ErrorType != "" && o.ErrorType != "none" {
			rule := "request_error"
			switch o.ErrorType {
			case "connection_refused", "backend_unavailable", "timeout", "backend_crash":
				rule = "backend_unavailable"
			}
			explanation, known := errorExplanations[o.ErrorType]
			if known {
				add(rule, "fact", explanation, Evidence{Kind: "proxy_error", ErrorType: o.ErrorType})
			} else {
				insufficient(rule, "Категория ошибки неизвестна; источник не назначается предположительно.")
			}
		}
	}
	for _, request := range input.Live.Active {
		if !request.Stage.Valid() || request.Stage.Terminal() {
			continue
		}
		var activity *Activity
		for _, a := range input.Activities {
			if a.RequestID == request.RequestID && domain.TechnicalIdentifier(a.SourceVersion) != "" && !a.ObservedAt.Before(request.StartedAt) && (input.CapturedAt.IsZero() || !a.ObservedAt.After(input.CapturedAt)) && (a.State == "working" || a.State == "stalled") && (activity == nil || a.ObservedAt.After(activity.ObservedAt)) {
				copy := a
				activity = &copy
			}
		}
		stage := request.Stage
		if activity != nil && activity.State == "stalled" {
			add("confirmed_stall", "fact", "Typed backend source явно сообщил stall для этого запроса на указанное время.", Evidence{Kind: "backend_activity", Activity: activity}, Evidence{Kind: "active_stage", Stage: &stage})
			continue
		}
		evidence := []Evidence{{Kind: "active_stage", Stage: &stage}, metricEvidence("active_elapsed_milliseconds", request.Elapsed)}
		explanation := "Запрос активен; сама длительность и stage не доказывают зависание."
		if activity != nil {
			explanation = "Backend явно сообщил продолжающуюся работу этого запроса на указанное время."
			evidence = append(evidence, Evidence{Kind: "backend_activity", Activity: activity})
		}
		add("active_work", "fact", explanation, evidence...)
		if activity == nil && valid(request.Elapsed, domain.Milliseconds) && *request.Elapsed.Value >= r.options.StallAssessmentMS {
			insufficient("confirmed_stall", "Запрос длительный, но typed backend stall signal отсутствует; зависание не подтверждено.")
		}
	}
	if len(result) == 0 {
		insufficient("request_error", "Нет данных для диагностического вывода либо versioned thresholds не достигнуты.")
	}
	return result
}

var errorExplanations = map[string]string{
	"connection_refused":  "Backend отклонил подключение до получения ответа.",
	"model_loading":       "Backend вернул HTTP 503: категория «загрузка модели / service unavailable» по versioned rule; фактическая загрузка процесса не доказана.",
	"http_api_error":      "Backend вернул HTTP/API error; текст ответа не сохраняется как evidence.",
	"timeout":             "Зафиксирован typed timeout подключения или ответа backend.",
	"context_overflow":    "Backend вернул HTTP 413 либо allowlisted context-overflow code.",
	"client_cancellation": "Клиент отменил запрос; это не считается сбоем backend или Inspector.",
	"backend_crash":       "Соединение backend оборвалось после начала ответа; падение процесса без process evidence не утверждается.",
	"backend_unavailable": "Backend недоступен; данных для более точной категории недостаточно.",
	"relay_failure":       "Передача ответа прервана; источник сбоя не доказан.",
	"inspector_failure":   "Typed internal source сообщил ошибку Inspector.",
}
