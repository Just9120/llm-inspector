package history

import (
	"context"
	"encoding/json"
	"math"
	"sort"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

const MinimumSamples = 3
const RecurringMinimum = 2

type Aggregate struct {
	SampleCount int      `json:"sample_count"`
	Sufficient  bool     `json:"is_statistically_sufficient"`
	Mean        *float64 `json:"arithmetic_mean"`
	Median      *float64 `json:"median"`
	P95         *float64 `json:"p95"`
}

// Calculate uses nearest-rank P95 (ceil(0.95*n)-1), not interpolation.
// Values remain visible below n=3 but are explicitly insufficient evidence.
func Calculate(samples []float64) Aggregate {
	values := make([]float64, 0, len(samples))
	for _, v := range samples {
		if !math.IsNaN(v) && !math.IsInf(v, 0) {
			values = append(values, v)
		}
	}
	a := Aggregate{SampleCount: len(values), Sufficient: len(values) >= MinimumSamples}
	if len(values) == 0 {
		return a
	}
	sort.Float64s(values)
	mean := 0.0
	// Incremental mean avoids both a large intermediate sum and repeated
	// division error for common integer counters. Opposite signs use scaling.
	for i, v := range values {
		n := float64(i + 1)
		if math.Signbit(mean) != math.Signbit(v) {
			mean = mean*(1-1/n) + v/n
		} else {
			mean += (v - mean) / n
		}
	}
	median := values[len(values)/2]
	if len(values)%2 == 0 {
		median = values[len(values)/2-1]/2 + median/2
	}
	p95 := values[int(math.Ceil(float64(len(values))*.95))-1]
	a.Mean = &mean
	a.Median = &median
	a.P95 = &p95
	return a
}

type Comparison struct {
	Metric               string           `json:"metric"`
	Baseline             Aggregate        `json:"baseline"`
	Candidate            Aggregate        `json:"candidate"`
	MeanDelta            *float64         `json:"mean_delta"`
	ConfirmedDegradation bool             `json:"is_confirmed_degradation"`
	RecurringErrors      []ErrorFrequency `json:"recurring_error_frequency"`
}

func compareSamples(key string, baseline, candidate []float64) Comparison {
	r := Comparison{Metric: key, Baseline: Calculate(baseline), Candidate: Calculate(candidate), RecurringErrors: []ErrorFrequency{}}
	if r.Baseline.Mean == nil || r.Candidate.Mean == nil {
		return r
	}
	delta := *r.Candidate.Mean - *r.Baseline.Mean
	r.MeanDelta = &delta
	if r.Baseline.Sufficient && r.Candidate.Sufficient {
		switch key {
		case "ttft_ms", "model_load_ms", "queue_ms", "total_duration_ms", "system_cpu_percent", "system_memory_percent", "error_rate_percent":
			r.ConfirmedDegradation = delta > 0
		case "prompt_tokens_per_second", "generation_tokens_per_second":
			r.ConfirmedDegradation = delta < 0
		}
	}
	return r
}

type ErrorGroup struct {
	Type        string    `json:"error_type"`
	Occurrences int       `json:"occurrences"`
	First       time.Time `json:"first_observed_at"`
	Last        time.Time `json:"last_observed_at"`
	Recurring   bool      `json:"is_recurring"`
}
type ErrorFrequency struct {
	Type                 string  `json:"error_type"`
	BaselineOccurrences  int     `json:"baseline_occurrences"`
	CandidateOccurrences int     `json:"candidate_occurrences"`
	BaselineRate         float64 `json:"baseline_rate_percent"`
	CandidateRate        float64 `json:"candidate_rate_percent"`
	Delta                float64 `json:"rate_delta_percentage_points"`
}
type ErrorCorrelation struct {
	Basis       string    `json:"basis"`
	ID          string    `json:"id"`
	Occurrences int       `json:"occurrences"`
	Types       []string  `json:"error_types"`
	First       time.Time `json:"first_observed_at"`
	Last        time.Time `json:"last_observed_at"`
}
type RuntimeGroup struct {
	Facts        domain.RuntimeFacts  `json:"facts"`
	First        time.Time            `json:"first_observed_at"`
	Last         time.Time            `json:"last_observed_at"`
	RequestCount int                  `json:"request_count"`
	Metrics      map[string]Aggregate `json:"metrics"`
}
type RuntimeCorrelation struct {
	Status         string         `json:"status"`
	Configurations []RuntimeGroup `json:"configurations"`
	Performance    []Comparison   `json:"performance_comparisons"`
	ErrorRate      *Comparison    `json:"error_rate_comparison"`
	MissingFacts   int            `json:"missing_facts"`
}
type Trend struct {
	Day     string               `json:"day"`
	Metrics map[string]Aggregate `json:"metrics"`
}
type ModelLoads struct {
	Cold        int `json:"cold_requests"`
	Warm        int `json:"warm_requests"`
	Unavailable int `json:"unavailable_requests"`
}
type Analytics struct {
	Filter             Filter               `json:"filter"`
	Metrics            map[string]Aggregate `json:"metrics"`
	Trend              []Trend              `json:"trend"`
	ModelLoads         ModelLoads           `json:"model_loads"`
	Errors             []ErrorGroup         `json:"error_groups"`
	ErrorCorrelations  []ErrorCorrelation   `json:"error_correlations"`
	UncorrelatedErrors int                  `json:"uncorrelated_errors"`
	Runtime            RuntimeCorrelation   `json:"runtime_correlation"`
}

func (s *Store) Analyze(ctx context.Context, f Filter) (Analytics, error) {
	slice, err := s.Slice(ctx, f)
	if err != nil {
		return Analytics{}, err
	}
	if slice.RequestsTruncated || slice.ResourcesTruncated {
		return Analytics{}, ErrTooLarge
	}
	return AnalyzeSlice(f, slice), nil
}
func (s *Store) Compare(ctx context.Context, baseline, candidate Filter, key string) (Comparison, error) {
	if !knownMetric(key) {
		return Comparison{}, ErrInvalid
	}
	b, err := s.Slice(ctx, baseline)
	if err != nil {
		return Comparison{}, err
	}
	c, err := s.Slice(ctx, candidate)
	if err != nil {
		return Comparison{}, err
	}
	if b.RequestsTruncated || b.ResourcesTruncated || c.RequestsTruncated || c.ResourcesTruncated {
		return Comparison{}, ErrTooLarge
	}
	r := compareSamples(key, allSamples(b)[key], allSamples(c)[key])
	r.RecurringErrors = compareErrors(b.Requests, c.Requests)
	return r, nil
}

func knownMetric(key string) bool {
	if key == "total_duration_ms" || key == "error_rate_percent" {
		return true
	}
	o := domain.Observation{}
	for _, f := range requestFields(&o) {
		if f.key == key {
			return true
		}
	}
	r := domain.ResourceSample{}
	for _, f := range resourceFields(&r) {
		if f.key == key {
			return true
		}
	}
	return false
}
func allSamples(s Slice) map[string][]float64 {
	result := map[string][]float64{"error_rate_percent": {}, "total_duration_ms": {}}
	o := domain.Observation{}
	for _, f := range requestFields(&o) {
		result[f.key] = []float64{}
	}
	d := domain.ResourceSample{}
	for _, f := range resourceFields(&d) {
		result[f.key] = []float64{}
	}
	for _, r := range s.Requests {
		for key, m := range r.Metrics {
			if knownMetric(key) && m.Validate() == nil && m.Value != nil {
				result[key] = append(result[key], *m.Value)
			}
		}
		rate := 0.0
		if r.ErrorType != "none" {
			rate = 100
		}
		result["error_rate_percent"] = append(result["error_rate_percent"], rate)
	}
	for i := range s.Resources {
		for _, f := range resourceFields(&s.Resources[i]) {
			if f.value.Validate() == nil && f.value.Value != nil {
				result[f.key] = append(result[f.key], *f.value.Value)
			}
		}
	}
	return result
}
func aggregateSlice(s Slice) map[string]Aggregate {
	result := map[string]Aggregate{}
	for key, v := range allSamples(s) {
		result[key] = Calculate(v)
	}
	return result
}

func AnalyzeSlice(f Filter, s Slice) Analytics {
	a := Analytics{Filter: f, Metrics: aggregateSlice(s), Trend: []Trend{}, Errors: []ErrorGroup{}, ErrorCorrelations: []ErrorCorrelation{}}
	days := map[string]Slice{}
	errorsByType := map[string]ErrorGroup{}
	correlations := map[string]ErrorCorrelation{}
	for _, r := range s.Requests {
		day := r.StartedAt.UTC().Format("2006-01-02")
		ds := days[day]
		ds.Requests = append(ds.Requests, r)
		days[day] = ds
		switch r.Telemetry.ModelLoad {
		case "cold":
			a.ModelLoads.Cold++
		case "warm":
			a.ModelLoads.Warm++
		default:
			a.ModelLoads.Unavailable++
		}
		if r.ErrorType == "none" {
			continue
		}
		g := errorsByType[r.ErrorType]
		g.Type = r.ErrorType
		g.Occurrences++
		bounds(&g.First, &g.Last, r.StartedAt)
		g.Recurring = g.Occurrences >= RecurringMinimum
		errorsByType[r.ErrorType] = g
		basis := "operation"
		correlationID := r.OperationID
		if correlationID == "" {
			basis = "session"
			correlationID = r.SessionID
		}
		if correlationID == "" {
			a.UncorrelatedErrors++
			continue
		}
		key := basis + ":" + correlationID
		c := correlations[key]
		c.Basis = basis
		c.ID = correlationID
		c.Occurrences++
		bounds(&c.First, &c.Last, r.StartedAt)
		present := false
		for _, v := range c.Types {
			if v == r.ErrorType {
				present = true
			}
		}
		if !present {
			c.Types = append(c.Types, r.ErrorType)
			sort.Strings(c.Types)
		}
		correlations[key] = c
	}
	for _, r := range s.Resources {
		day := r.CapturedAt.UTC().Format("2006-01-02")
		ds := days[day]
		ds.Resources = append(ds.Resources, r)
		days[day] = ds
	}
	for day, ds := range days {
		a.Trend = append(a.Trend, Trend{Day: day, Metrics: aggregateSlice(ds)})
	}
	sort.Slice(a.Trend, func(i, j int) bool { return a.Trend[i].Day < a.Trend[j].Day })
	for _, g := range errorsByType {
		a.Errors = append(a.Errors, g)
	}
	sort.Slice(a.Errors, func(i, j int) bool { return a.Errors[i].Type < a.Errors[j].Type })
	for _, c := range correlations {
		if c.Occurrences >= 2 {
			a.ErrorCorrelations = append(a.ErrorCorrelations, c)
		} else {
			a.UncorrelatedErrors += c.Occurrences
		}
	}
	sort.Slice(a.ErrorCorrelations, func(i, j int) bool {
		return a.ErrorCorrelations[i].Basis+a.ErrorCorrelations[i].ID < a.ErrorCorrelations[j].Basis+a.ErrorCorrelations[j].ID
	})
	a.Runtime = correlateRuntime(s.Requests)
	return a
}
func bounds(first, last *time.Time, at time.Time) {
	if first.IsZero() || at.Before(*first) {
		*first = at
	}
	if last.IsZero() || at.After(*last) {
		*last = at
	}
}
func compareErrors(b, c []Request) []ErrorFrequency {
	bc, cc := map[string]int{}, map[string]int{}
	keys := map[string]bool{}
	for _, r := range b {
		if r.ErrorType != "none" {
			bc[r.ErrorType]++
			keys[r.ErrorType] = true
		}
	}
	for _, r := range c {
		if r.ErrorType != "none" {
			cc[r.ErrorType]++
			keys[r.ErrorType] = true
		}
	}
	result := []ErrorFrequency{}
	for key := range keys {
		if bc[key] < RecurringMinimum && cc[key] < RecurringMinimum {
			continue
		}
		v := ErrorFrequency{Type: key, BaselineOccurrences: bc[key], CandidateOccurrences: cc[key]}
		if len(b) > 0 {
			v.BaselineRate = 100 * float64(bc[key]) / float64(len(b))
		}
		if len(c) > 0 {
			v.CandidateRate = 100 * float64(cc[key]) / float64(len(c))
		}
		v.Delta = v.CandidateRate - v.BaselineRate
		result = append(result, v)
	}
	sort.Slice(result, func(i, j int) bool { return result[i].Type < result[j].Type })
	return result
}
func correlateRuntime(requests []Request) RuntimeCorrelation {
	r := RuntimeCorrelation{Status: "no_runtime_facts", Configurations: []RuntimeGroup{}, Performance: []Comparison{}}
	groups := map[string][]Request{}
	facts := map[string]domain.RuntimeFacts{}
	for _, q := range requests {
		if q.Runtime == nil || !q.Runtime.Valid() {
			r.MissingFacts++
			continue
		}
		b, _ := json.Marshal(q.Runtime)
		key := string(b)
		groups[key] = append(groups[key], q)
		facts[key] = *q.Runtime
	}
	for key, reqs := range groups {
		g := RuntimeGroup{Facts: facts[key], RequestCount: len(reqs), Metrics: aggregateSlice(Slice{Requests: reqs})}
		for _, q := range reqs {
			bounds(&g.First, &g.Last, q.StartedAt)
		}
		r.Configurations = append(r.Configurations, g)
	}
	sort.Slice(r.Configurations, func(i, j int) bool {
		a, b := r.Configurations[i], r.Configurations[j]
		if a.First.Equal(b.First) {
			x, _ := json.Marshal(a.Facts)
			y, _ := json.Marshal(b.Facts)
			return string(x) < string(y)
		}
		return a.First.Before(b.First)
	})
	if len(r.Configurations) == 0 {
		return r
	}
	r.Status = "single_configuration"
	if len(r.Configurations) == 1 {
		return r
	}
	base, last := r.Configurations[0], r.Configurations[len(r.Configurations)-1]
	bj, _ := json.Marshal(base.Facts)
	cj, _ := json.Marshal(last.Facts)
	b, c := groups[string(bj)], groups[string(cj)]
	bs, cs := allSamples(Slice{Requests: b}), allSamples(Slice{Requests: c})
	for _, key := range []string{"total_duration_ms", "ttft_ms", "prompt_tokens_per_second", "generation_tokens_per_second"} {
		r.Performance = append(r.Performance, compareSamples(key, bs[key], cs[key]))
	}
	er := compareSamples("error_rate_percent", bs["error_rate_percent"], cs["error_rate_percent"])
	er.RecurringErrors = compareErrors(b, c)
	r.ErrorRate = &er
	r.Status = "insufficient_samples"
	if len(b) >= MinimumSamples && len(c) >= MinimumSamples {
		r.Status = "sufficient"
	}
	return r
}
