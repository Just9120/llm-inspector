package history

import (
	"encoding/json"
	"math"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestStatisticsAndDegradationBoundaries(t *testing.T) {
	for _, tc := range []struct {
		v                 []float64
		n                 int
		mean, median, p95 float64
		sufficient        bool
	}{
		{[]float64{1}, 1, 1, 1, 1, false}, {[]float64{1, 3}, 2, 2, 2, 3, false}, {[]float64{1, 2, 3}, 3, 2, 2, 3, true}, {[]float64{2, 4, 6, 8}, 4, 5, 5, 8, true},
	} {
		got := Calculate(tc.v)
		if got.SampleCount != tc.n || got.Sufficient != tc.sufficient || *got.Mean != tc.mean || *got.Median != tc.median || *got.P95 != tc.p95 {
			t.Fatalf("%+v", got)
		}
	}
	if a := Calculate(nil); a.Mean != nil || a.Median != nil || a.P95 != nil || a.Sufficient {
		t.Fatal(a)
	}
	values := make([]float64, 20)
	for i := range values {
		values[i] = float64(i + 1)
	}
	if *Calculate(values).P95 != 19 {
		t.Fatal("P95 is not nearest-rank")
	}
	if Calculate([]float64{math.NaN(), math.Inf(1)}).SampleCount != 0 {
		t.Fatal("invalid samples accepted")
	}
	if a := Calculate([]float64{math.MaxFloat64, math.MaxFloat64, math.MaxFloat64}); a.Mean == nil || math.IsInf(*a.Mean, 0) {
		t.Fatal("finite aggregate overflow")
	}
	for _, key := range []string{"ttft_ms", "model_load_ms", "queue_ms", "total_duration_ms", "system_cpu_percent", "system_memory_percent", "error_rate_percent"} {
		if !compareSamples(key, []float64{1, 2, 3}, []float64{4, 5, 6}).ConfirmedDegradation {
			t.Fatal(key)
		}
	}
	for _, key := range []string{"prompt_tokens_per_second", "generation_tokens_per_second"} {
		if !compareSamples(key, []float64{4, 5, 6}, []float64{1, 2, 3}).ConfirmedDegradation {
			t.Fatal(key)
		}
	}
	if compareSamples("input_tokens", []float64{1, 2, 3}, []float64{4, 5, 6}).ConfirmedDegradation || compareSamples("ttft_ms", []float64{1, 2}, []float64{4, 5, 6}).ConfirmedDegradation {
		t.Fatal("unsupported/insufficient claim")
	}
}

func TestAnalyticsTrendsComparisonsErrorsAndRuntime(t *testing.T) {
	s := testStore(t)
	start := observation(1).StartedAt
	for i := 1; i <= 6; i++ {
		o := observation(i)
		o.StartedAt = start.Add(time.Duration(i) * time.Minute)
		if i <= 3 {
			o.Runtime = &domain.RuntimeFacts{ConfigurationID: "old", BackendVersion: "1.0"}
			o.DurationMS = 100
			o.Telemetry.Model = "model-a"
		} else {
			o.Runtime = &domain.RuntimeFacts{ConfigurationID: "new", BackendVersion: "2.0"}
			o.DurationMS = 200
			o.Telemetry.Model = "model-b"
			o.ErrorType = "timeout"
			o.ErrorOrigin = "backend"
		}
		o.Correlation = &domain.Correlation{SessionID: "00000000000000000000000000000064", TurnID: o.RequestID, Sequence: i}
		if err := s.Record(t.Context(), o); err != nil {
			t.Fatal(err)
		}
	}
	r := resource(1, start)
	if err := s.RecordResources(t.Context(), []domain.ResourceSample{r}); err != nil {
		t.Fatal(err)
	}
	a, err := s.Analyze(t.Context(), Filter{})
	if err != nil {
		t.Fatal(err)
	}
	if len(a.Trend) != 1 || *a.Metrics["total_duration_ms"].Mean != 150 || *a.Metrics["error_rate_percent"].Mean != 50 || len(a.Errors) != 1 || !a.Errors[0].Recurring || a.Errors[0].Occurrences != 3 {
		t.Fatalf("bad analytics %+v", a)
	}
	if len(a.ErrorCorrelations) != 1 || a.ErrorCorrelations[0].Basis != "session" || a.UncorrelatedErrors != 0 || a.Runtime.Status != "sufficient" || len(a.Runtime.Configurations) != 2 || !a.Runtime.Performance[0].ConfirmedDegradation || !a.Runtime.ErrorRate.ConfirmedDegradation {
		t.Fatalf("correlation %+v", a)
	}
	c, err := s.Compare(t.Context(), Filter{Model: "model-a"}, Filter{Model: "model-b"}, "total_duration_ms")
	if err != nil || !c.ConfirmedDegradation || *c.MeanDelta != 100 || len(c.RecurringErrors) != 1 || c.RecurringErrors[0].Delta != 100 {
		t.Fatal(c, err)
	}
	if _, err = s.Compare(t.Context(), Filter{}, Filter{}, "private content"); err == nil {
		t.Fatal("metric allowlist bypass")
	}
	if _, err = json.Marshal(a); err != nil {
		t.Fatal(err)
	}
	if correlateRuntime(nil).Status != "no_runtime_facts" {
		t.Fatal("empty correlation fabricated")
	}
}

func TestRuntimeComparisonUsesAllFactsNotOnlyConfigurationID(t *testing.T) {
	requests := []Request{}
	for i := 1; i <= 4; i++ {
		o := observation(i)
		version := "1"
		if i > 2 {
			version = "2"
		}
		o.Runtime = &domain.RuntimeFacts{ConfigurationID: "same", BackendVersion: version}
		requests = append(requests, Request{Observation: o, Metrics: map[string]domain.Metric{}})
	}
	r := correlateRuntime(requests)
	if r.Status != "insufficient_samples" || len(r.Configurations) != 2 {
		t.Fatal(r)
	}
	if r.ErrorRate.ConfirmedDegradation {
		t.Fatal("insufficient runtime data claimed")
	}
}
