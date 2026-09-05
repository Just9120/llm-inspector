package validation

import (
	"context"
	"encoding/json"
	"errors"
	"math"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/performance"
)

func testOptions() Options {
	return Options{SourceSHA: strings.Repeat("a", 40), ExecutableSHA: strings.Repeat("b", 64), Profile: performance.Balanced, Phase: "pilot", Duration: 200 * time.Millisecond, Interval: 200 * time.Millisecond, LogicalCPUs: 2}
}

func TestCaptureBoundsRejectUnboundedOrUnidentifiedWork(t *testing.T) {
	o := testOptions()
	if err := o.Validate(); err != nil {
		t.Fatal(err)
	}
	for _, mutate := range []func(*Options){
		func(o *Options) { o.SourceSHA = "main" }, func(o *Options) { o.ExecutableSHA = "unknown" },
		func(o *Options) { o.Profile = performance.Custom }, func(o *Options) { o.Profile = "invalid" },
		func(o *Options) { o.Phase = "release_pass" }, func(o *Options) { o.Duration = 3 * time.Hour },
		func(o *Options) { o.Duration = 0 }, func(o *Options) { o.Interval = time.Millisecond },
		func(o *Options) { o.Interval = 11 * time.Second }, func(o *Options) { o.LogicalCPUs = 0 },
	} {
		invalid := o
		mutate(&invalid)
		if invalid.Validate() == nil {
			t.Fatal("invalid capture accepted", invalid)
		}
	}
}

func samples() []Sample {
	return []Sample{
		{ElapsedSeconds: 0, Counters: &Counters{CPU100ns: 1e7, PrivateBytes: 300, IOWriteBytes: 50, Live: 2, Retained: 2}},
		{ElapsedSeconds: 1, Counters: &Counters{CPU100ns: 11e6, PrivateBytes: 500, IOWriteBytes: 60, Live: 2, Retained: 2}},
		{ElapsedSeconds: 4, Counters: &Counters{CPU100ns: 17e6, PrivateBytes: 200, IOWriteBytes: 100, Live: 1, Retained: 2}},
	}
}

func TestCaptureSummaryWeightsCPUIntervalsAndUsesPrivateBytes(t *testing.T) {
	s := Summarize(samples(), 2)
	if !s.Complete || *s.CPUCapacityMeanPercent != 8.75 || *s.CPUCapacityP95Percent != 10 || *s.PrivateBytesP95 != 500 || *s.PrivateGrowthBytes != -100 || *s.IOWriteBytes != 50 || s.MeasuredSeconds != 4 {
		t.Fatalf("incorrect summary %+v", s)
	}
	if s := Summarize(samples(), 0); s.Complete {
		t.Fatal("invalid CPU count")
	}
	if s := Summarize(samples()[:1], 2); s.Complete || s.CPUCapacityMeanPercent != nil {
		t.Fatal("one point is not a rate")
	}
}

func TestCaptureSummaryFailsClosedForMissingResetAndInvalidSamples(t *testing.T) {
	for _, mutate := range []func([]Sample){
		func(s []Sample) { s[1].Counters = nil }, func(s []Sample) { s[1].ErrorCode = "gap" },
		func(s []Sample) { s[1].ElapsedSeconds = 0 }, func(s []Sample) { s[1].ElapsedSeconds = math.NaN() },
		func(s []Sample) { s[1].ElapsedSeconds = math.Inf(1) }, func(s []Sample) { s[0].ElapsedSeconds = -1 },
		func(s []Sample) { s[1].Counters.CPU100ns = 0 }, func(s []Sample) { s[1].Counters.IOWriteBytes = 0 },
		func(s []Sample) { s[1].Counters.Live = 0 }, func(s []Sample) { s[1].Counters.Retained = 0 },
		func(s []Sample) { s[1].Counters.CPU100ns = 1e12 },
	} {
		s := samples()
		mutate(s)
		got := Summarize(s, 2)
		if got.Complete || got.PrivateBytesP95 != nil || got.CPUCapacityMeanPercent != nil {
			t.Fatal("invalid samples generated a result")
		}
	}
}

type sourceFunc func() (Counters, error)

func (f sourceFunc) Read() (Counters, error) { return f() }

func TestCaptureActualElapsedCancellationAndErrorPrivacy(t *testing.T) {
	calls := 0
	r, err := Capture(t.Context(), testOptions(), sourceFunc(func() (Counters, error) {
		calls++
		return Counters{CPU100ns: uint64(calls), PrivateBytes: 12, Live: 1, Retained: 1}, nil
	}))
	if err != nil || r.TerminalStatus != "completed" || !r.Summary.Complete || r.Summary.MeasuredSeconds < .19 || r.ReleaseEvidence || len(r.Limitations) < 5 {
		t.Fatal(r, err)
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	r, err = Capture(ctx, testOptions(), sourceFunc(func() (Counters, error) { t.Fatal("cancelled source called"); return Counters{}, nil }))
	if err != nil || r.TerminalStatus != "cancelled" || r.Summary.Complete || r.ReleaseEvidence {
		t.Fatal(r, err)
	}
	r, err = Capture(t.Context(), testOptions(), sourceFunc(func() (Counters, error) { return Counters{}, errors.New("private-user-path-canary") }))
	encoded, _ := json.Marshal(r)
	if err != nil || r.TerminalStatus != "counter_source_failed" || r.Summary.Complete || strings.Contains(string(encoded), "private-user-path-canary") {
		t.Fatal("raw error leak or false success")
	}
	if _, err = Capture(t.Context(), testOptions(), nil); err == nil {
		t.Fatal("nil source")
	}
}
