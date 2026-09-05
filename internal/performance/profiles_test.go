package performance

import (
	"math"
	"testing"
	"time"
)

func TestCanonicalProfileBudgetsAndIndependentCopies(t *testing.T) {
	expected := [][16]float64{{1.5, 4, 192 * mib, 16 * mib, 1, 3, 128 * mib, mib, 3, 5, .25, 1, 8 * mib, mib / 4, 2, 8}, {3, 8, 256 * mib, 32 * mib, 2, 5, 192 * mib, 2 * mib, 5, 10, .5, 2, 16 * mib, mib, 5, 15}, {5, 12, 384 * mib, 64 * mib, 3, 8, 256 * mib, 5 * mib, 8, 15, 1, 4, 32 * mib, 5 * mib, 15, 30}}
	for i, p := range BuiltIn() {
		if len(p.ReleaseBudget) != 16 || p.Name == "" {
			t.Fatal(p)
		}
		for j, name := range metricNames {
			if p.ReleaseBudget[name] != expected[i][j] {
				t.Fatalf("%s/%s", p.ID, name)
			}
		}
		p.ReleaseBudget[metricNames[0]] = 999
	}
	if BuiltIn()[0].ReleaseBudget[metricNames[0]] != 1.5 {
		t.Fatal("shared budget mutation")
	}
	for i, d := range []time.Duration{2 * time.Second, time.Second, 500 * time.Millisecond} {
		if BuiltIn()[i].Interval() != d {
			t.Fatal("sampling drift")
		}
	}
	for _, n := range []int{250, 1000, 10000} {
		p, err := Resolve(Custom, n)
		if err != nil || p.ReleaseBudget != nil || p.SamplingMilliseconds != n {
			t.Fatal(p, err)
		}
	}
	for _, n := range []int{-1, 249, 10001} {
		if _, err := Resolve(Custom, n); err == nil {
			t.Fatal("invalid custom")
		}
	}
	if _, err := Resolve("alien", 1000); err == nil {
		t.Fatal("unknown profile")
	}
}
func validProtocol() Protocol {
	return Protocol{IdleWarmup: 10 * time.Minute, IdleMeasurement: time.Hour, ActivePairOrders: []PairOrder{BaselineFirst, InspectorFirst, BaselineFirst, InspectorFirst, BaselineFirst}, ReliableDiscreteGPU: true}
}
func boundaryMeasurements(id ProfileID) map[string]*float64 {
	p, _ := Resolve(id, 1000)
	m := map[string]*float64{}
	for k, v := range p.ReleaseBudget {
		value := v
		m[k] = &value
	}
	return m
}
func TestPerformanceGateEveryMetricBoundaryAndMissing(t *testing.T) {
	for _, p := range BuiltIn() {
		m := boundaryMeasurements(p.ID)
		r := Evaluate(p.ID, validProtocol(), m)
		if !r.Passed || len(r.Findings) != 20 {
			t.Fatal(r)
		}
		for _, name := range metricNames {
			baseline := *m[name]
			v := baseline + .01
			m[name] = &v
			if Evaluate(p.ID, validProtocol(), m).Passed {
				t.Fatal("over budget", name)
			}
			m[name] = nil
			if Evaluate(p.ID, validProtocol(), m).Passed {
				t.Fatal("missing passed", name)
			}
			m[name] = &baseline
		}
	}
}
func TestPerformanceProtocolFailClosedAndGPUApplicability(t *testing.T) {
	p := validProtocol()
	m := boundaryMeasurements(Balanced)
	cases := []Protocol{p, p, p, p, p}
	cases[0].IdleWarmup -= time.Nanosecond
	cases[1].IdleMeasurement -= time.Nanosecond
	cases[2].ActivePairOrders = p.ActivePairOrders[:4]
	cases[3].ActivePairOrders = []PairOrder{BaselineFirst, BaselineFirst, InspectorFirst, BaselineFirst, InspectorFirst}
	cases[4].ActivePairOrders = []PairOrder{"invalid", InspectorFirst, BaselineFirst, InspectorFirst, BaselineFirst}
	for _, c := range cases {
		if Evaluate(Balanced, c, m).Passed {
			t.Fatal("invalid protocol passed")
		}
	}
	if Evaluate(Custom, p, m).Passed || Evaluate("unknown", p, m).Passed {
		t.Fatal("unapproved budget")
	}
	for _, n := range metricNames[4:7] {
		m[n] = nil
	}
	if Evaluate(Balanced, p, m).Passed {
		t.Fatal("mandatory GPU")
	}
	p.ReliableDiscreteGPU = false
	r := Evaluate(Balanced, p, m)
	if !r.Passed {
		t.Fatal(r)
	}
	count := 0
	for _, f := range r.Findings {
		if f.Status == NotApplicable {
			count++
		}
	}
	if count != 3 {
		t.Fatal(count)
	}
	for _, v := range []float64{math.NaN(), math.Inf(1), math.Inf(-1)} {
		m[metricNames[0]] = &v
		if Evaluate(Balanced, p, m).Passed {
			t.Fatal("non-finite passed")
		}
	}
}
