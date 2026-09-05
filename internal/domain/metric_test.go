package domain

import (
	"encoding/json"
	"math"
	"testing"
)

func TestMetricInvariants(t *testing.T) {
	for _, v := range []float64{-1, math.NaN(), math.Inf(1), 1.5} {
		if m := Measured(v, Tokens, "test", "v1"); m.Value != nil || m.Quality != Unavailable {
			t.Fatalf("invalid token metric accepted: %v", v)
		}
	}
	if Measured(101, Percent, "test", "v1").Value != nil {
		t.Fatal("percent over 100")
	}
	if Measured(-2, TokenDelta, "test", "v1").Value == nil {
		t.Fatal("signed context delta rejected")
	}
	if Derived(2, Count, Calculated, "v1", "").Value != nil {
		t.Fatal("missing derivation accepted")
	}
	m := Missing(Tokens, "test", "v1")
	data, _ := json.Marshal(m)
	var read Metric
	if json.Unmarshal(data, &read) != nil || read.Validate() != nil || read.Value != nil {
		t.Fatal("unavailable roundtrip")
	}
}

func TestTechnicalIdentifier(t *testing.T) {
	for _, v := range []string{"hello world", "secret\nvalue", "<script>", "", string(make([]byte, 129))} {
		if TechnicalIdentifier(v) != "" {
			t.Fatal("unsafe identifier")
		}
	}
	if TechnicalIdentifier("orcarouter/Qwen3.8-27B:q4_K_M") == "" {
		t.Fatal("model rejected")
	}
}
