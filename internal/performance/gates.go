package performance

import (
	"math"
	"time"
)

type PairOrder string

const (
	BaselineFirst  PairOrder = "baseline_then_inspector"
	InspectorFirst PairOrder = "inspector_then_baseline"
)

type Protocol struct {
	IdleWarmup          time.Duration
	IdleMeasurement     time.Duration
	ActivePairOrders    []PairOrder
	ReliableDiscreteGPU bool
}
type Status string

const (
	Passed        Status = "passed"
	Failed        Status = "failed"
	Unavailable   Status = "unavailable"
	NotApplicable Status = "not_applicable"
)

type Finding struct {
	Metric    string   `json:"metric"`
	Status    Status   `json:"status"`
	Observed  *float64 `json:"observed"`
	Threshold *float64 `json:"threshold"`
	Detail    string   `json:"detail"`
}
type Result struct {
	Profile  ProfileID `json:"profile"`
	Findings []Finding `json:"findings"`
	Passed   bool      `json:"passed"`
}

// Resolve canonical budgets internally: callers cannot enlarge a budget to pass.
func Evaluate(id ProfileID, p Protocol, measurement map[string]*float64) Result {
	result := Result{Profile: id, Findings: []Finding{}}
	profile, err := Resolve(id, 1000)
	if err != nil || profile.ReleaseBudget == nil {
		result.Findings = append(result.Findings, Finding{Metric: "release_profile", Status: Failed, Detail: "Release Evidence требует встроенного профиля."})
		return result
	}
	addProtocol := func(name string, observed, required float64, valid bool, detail string) {
		s := Failed
		if valid {
			s = Passed
		}
		result.Findings = append(result.Findings, Finding{name, s, &observed, &required, detail})
	}
	addProtocol("idle_warmup_minutes", p.IdleWarmup.Minutes(), 10, p.IdleWarmup >= 10*time.Minute, "Прогрев idle — минимум 10 минут.")
	addProtocol("idle_measurement_minutes", p.IdleMeasurement.Minutes(), 60, p.IdleMeasurement >= time.Hour, "Измерение idle — минимум 60 минут.")
	addProtocol("paired_repetitions", float64(len(p.ActivePairOrders)), 5, len(p.ActivePairOrders) >= 5, "Нужно минимум 5 парных повторов.")
	alternating := len(p.ActivePairOrders) >= 5
	for i, order := range p.ActivePairOrders {
		if order != BaselineFirst && order != InspectorFirst || i > 0 && order == p.ActivePairOrders[i-1] {
			alternating = false
		}
	}
	status := Failed
	if alternating {
		status = Passed
	}
	result.Findings = append(result.Findings, Finding{Metric: "paired_order_alternation", Status: status, Detail: "Порядок пар должен чередоваться AB/BA."})
	for i, name := range metricNames {
		limit := profile.ReleaseBudget[name]
		var value *float64
		if input := measurement[name]; input != nil && !math.IsNaN(*input) && !math.IsInf(*input, 0) {
			v := *input
			value = &v
		}
		f := Finding{Metric: name, Status: Unavailable, Observed: value, Threshold: &limit, Detail: "Обязательная метрика недоступна и не может считаться пройденной."}
		if i >= 4 && i <= 6 && !p.ReliableDiscreteGPU {
			f.Status = NotApplicable
			f.Detail = "На этом host нет надёжного источника discrete GPU."
		} else if value != nil {
			f.Status = Passed
			if *value > limit {
				f.Status = Failed
			}
			f.Detail = "Значение должно быть не выше лимита профиля."
		}
		result.Findings = append(result.Findings, f)
	}
	result.Passed = true
	for _, f := range result.Findings {
		if f.Status != Passed && f.Status != NotApplicable {
			result.Passed = false
		}
	}
	return result
}
