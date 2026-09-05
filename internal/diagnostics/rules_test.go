package diagnostics

import (
	"encoding/json"
	"math"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func exact(v float64, u domain.Unit) domain.Metric {
	return domain.Measured(v, u, "backend_extension", "fixture-v1")
}
func fixtureInput() Input {
	t := domain.MissingTelemetry(domain.Ollama)
	t.PromptTokens = exact(8192, domain.Tokens)
	t.GenerationSpeed = exact(10, domain.TokensPerSecond)
	t.ContextUsage = exact(900, domain.Tokens)
	t.ContextLimit = exact(1000, domain.Tokens)
	t.ModelLoadTime = exact(1000, domain.Milliseconds)
	t.ModelLoad = "cold"
	t.QueueTime = exact(1000, domain.Milliseconds)
	r := domain.MissingResource()
	r.RequestID = "request"
	r.Process = &domain.ProcessAssociation{PID: 1, StartedAt: time.Unix(0, 0), ImageName: "ollama.exe", SourceVersion: "listener-process-v1"}
	r.ProcessCPU = exact(60, domain.Percent)
	r.GPUUtilization = exact(20, domain.Percent)
	r.GPUVRAMUsed = exact(90, domain.Bytes)
	r.GPUVRAMTotal = exact(100, domain.Bytes)
	return Input{Latest: &domain.Observation{RequestID: "request", Outcome: "completed", ErrorType: "none", Telemetry: t}, Resource: &r}
}
func find(result []Conclusion, rule string) *Conclusion {
	for _, c := range result {
		if c.Rule == rule {
			return &c
		}
	}
	return nil
}

func TestVersionedThresholdsAndEvidence(t *testing.T) {
	got := Default().Evaluate(fixtureInput())
	for _, rule := range []string{"large_prompt", "slow_generation", "cpu_offload", "vram_pressure", "model_loading_latency", "queue_waiting_latency", "high_context_usage"} {
		c := find(got, rule)
		want := "fact"
		if rule == "cpu_offload" {
			want = "hypothesis"
		}
		if c == nil || c.Kind != want || len(c.Evidence) == 0 || c.Explanation == "" || c.RuleVersion != "diagnostic-rules-v1" {
			t.Fatalf("missing explainable %s", rule)
		}
	}
}
func TestValuesOutsideThresholdsDoNotCreateFalsePositives(t *testing.T) {
	input := fixtureInput()
	v := &input.Latest.Telemetry
	v.PromptTokens = exact(8191, domain.Tokens)
	v.GenerationSpeed = exact(10.01, domain.TokensPerSecond)
	v.ContextUsage = exact(899, domain.Tokens)
	v.ModelLoad = "warm"
	v.QueueTime = exact(999, domain.Milliseconds)
	input.Resource.ProcessCPU = exact(59.99, domain.Percent)
	input.Resource.GPUVRAMUsed = exact(89, domain.Bytes)
	for _, c := range Default().Evaluate(input) {
		if c.Kind == "fact" || c.Kind == "hypothesis" {
			t.Fatalf("false positive: %s", c.Rule)
		}
	}
}
func TestMissingMismatchedAndEstimatedEvidence(t *testing.T) {
	input := fixtureInput()
	input.Resource.RequestID = "foreign"
	input.Latest.Telemetry = domain.MissingTelemetry(domain.Ollama)
	for _, c := range Default().Evaluate(input) {
		if c.Kind != "insufficient_data" {
			t.Fatal("mismatched evidence promoted")
		}
	}
	input.Latest.Telemetry.PromptTokens = domain.Derived(9000, domain.Tokens, domain.Estimated, "fixture-v1", "token-estimator-v1")
	if c := find(Default().Evaluate(input), "large_prompt"); c == nil || c.Kind != "hypothesis" {
		t.Fatal("estimate promoted to fact")
	}
	input = fixtureInput()
	input.Resource.Process = nil
	if c := find(Default().Evaluate(input), "cpu_offload"); c == nil || c.Kind != "insufficient_data" {
		t.Fatal("process attribution guessed")
	}
	input.Latest.Telemetry.PromptTokens = exact(9000, domain.Bytes)
	if c := find(Default().Evaluate(input), "large_prompt"); c == nil || c.Kind != "insufficient_data" {
		t.Fatal("wrong units accepted")
	}
}
func TestStallRequiresTypedRequestScopedSignal(t *testing.T) {
	start := time.Unix(0, 0)
	stage := domain.StageValue{Stage: domain.PromptProcessing, Evidence: "protocol_observed", SourceVersion: "gateway-v1"}
	input := Input{CapturedAt: start.Add(30 * time.Second), Live: domain.LiveSnapshot{Active: []domain.LiveRequest{{RequestID: "request", StartedAt: start, Stage: stage, Elapsed: domain.Derived(30000, domain.Milliseconds, domain.Calculated, "clock-v1", "elapsed-v1")}}}}
	if c := find(Default().Evaluate(input), "confirmed_stall"); c == nil || c.Kind != "insufficient_data" {
		t.Fatal("elapsed time fabricated stall")
	}
	for _, a := range []Activity{{"foreign", "stalled", start.Add(time.Second), "backend-v1"}, {"request", "stalled", start.Add(-time.Second), "backend-v1"}, {"request", "stalled", start.Add(time.Hour), "backend-v1"}, {"request", "stalled", start.Add(time.Second), ""}} {
		input.Activities = []Activity{a}
		if c := find(Default().Evaluate(input), "confirmed_stall"); c == nil || c.Kind != "insufficient_data" {
			t.Fatal("invalid activity evidence")
		}
	}
	input.Activities = []Activity{{"request", "stalled", start.Add(30 * time.Second), "backend-v1"}}
	if c := find(Default().Evaluate(input), "confirmed_stall"); c == nil || c.Kind != "fact" {
		t.Fatal("typed stall missing")
	}
	input.Activities[0].State = "working"
	if c := find(Default().Evaluate(input), "confirmed_stall"); c != nil {
		t.Fatal("working backend called stalled")
	}
}
func TestErrorsAndInvalidOptionsStayContentFree(t *testing.T) {
	for errorType := range errorExplanations {
		input := fixtureInput()
		input.Latest.ErrorType = errorType
		got := Default().Evaluate(input)
		found := false
		for _, c := range got {
			for _, e := range c.Evidence {
				found = found || e.ErrorType == errorType
			}
		}
		if !found {
			t.Fatal("missing typed error")
		}
	}
	input := fixtureInput()
	input.Latest.ErrorType = "PRIVATE_ERROR_PROMPT"
	b, _ := json.Marshal(Default().Evaluate(input))
	if strings.Contains(string(b), "PRIVATE_ERROR_PROMPT") {
		t.Fatal("error message escaped")
	}
	for _, mutate := range []func(*Options){func(o *Options) { o.Version = "" }, func(o *Options) { o.LargePrompt = 0 }, func(o *Options) { o.VRAMPressure = 101 }, func(o *Options) { o.OffloadGPU = math.NaN() }, func(o *Options) { o.QueueMS = math.Inf(1) }} {
		o := DefaultOptions()
		mutate(&o)
		if _, err := New(o); err == nil {
			t.Fatal("invalid threshold accepted")
		}
	}
	if Ratio(exact(101, domain.Bytes), exact(100, domain.Bytes)).Value != nil || Ratio(exact(1, domain.Bytes), exact(0, domain.Bytes)).Value != nil {
		t.Fatal("impossible ratio credited")
	}
}
