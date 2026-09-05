// Package domain defines content-free records shared by collectors and consumers.
package domain

import (
	"errors"
	"math"
	"strings"
)

type Quality string
type Unit string

const (
	Exact           Quality = "exact"
	Calculated      Quality = "calculated"
	Estimated       Quality = "estimated"
	Unavailable     Quality = "unavailable"
	Tokens          Unit    = "tokens"
	TokenDelta      Unit    = "token_delta"
	Milliseconds    Unit    = "milliseconds"
	TokensPerSecond Unit    = "tokens_per_second"
	Percent         Unit    = "percent"
	Count           Unit    = "count"
	Bytes           Unit    = "bytes"
	Celsius         Unit    = "celsius"
	Watts           Unit    = "watts"
)

// Metric has no free-form diagnostic text. Unavailable is always null, never zero.
type Metric struct {
	Value             *float64 `json:"value"`
	Unit              Unit     `json:"unit"`
	Quality           Quality  `json:"quality"`
	Source            string   `json:"source"`
	SourceVersion     string   `json:"source_version"`
	DerivationVersion string   `json:"derivation_version,omitempty"`
}

func Missing(unit Unit, source, version string) Metric {
	return Metric{Unit: unit, Quality: Unavailable, Source: source, SourceVersion: version}
}

func Measured(value float64, unit Unit, source, version string) Metric {
	m := Metric{Value: &value, Unit: unit, Quality: Exact, Source: source, SourceVersion: version}
	if m.Validate() != nil {
		return Missing(unit, source, version)
	}
	return m
}

func Derived(value float64, unit Unit, quality Quality, version, derivation string) Metric {
	m := Metric{Value: &value, Unit: unit, Quality: quality, Source: "inspector", SourceVersion: version, DerivationVersion: derivation}
	if (quality != Calculated && quality != Estimated) || m.Validate() != nil {
		return Missing(unit, "inspector", version)
	}
	return m
}

func (m Metric) Validate() error {
	invalid := errors.New("некорректная техническая метрика")
	if m.SourceVersion == "" || m.Source == "" {
		return invalid
	}
	switch m.Unit {
	case Tokens, TokenDelta, Milliseconds, TokensPerSecond, Percent, Count, Bytes, Celsius, Watts:
	default:
		return invalid
	}
	if m.Quality == Unavailable {
		if m.Value != nil || m.DerivationVersion != "" {
			return invalid
		}
		return nil
	}
	if m.Value == nil || math.IsNaN(*m.Value) || math.IsInf(*m.Value, 0) {
		return invalid
	}
	v := *m.Value
	if (v < 0 && m.Unit != TokenDelta) || (m.Unit == Percent && v > 100) {
		return invalid
	}
	if (m.Unit == Tokens || m.Unit == TokenDelta || m.Unit == Count || m.Unit == Bytes) && (math.Trunc(v) != v || math.Abs(v) > 9007199254740991) {
		return invalid
	}
	switch m.Quality {
	case Exact:
		if m.DerivationVersion != "" {
			return invalid
		}
	case Calculated, Estimated:
		if m.DerivationVersion == "" {
			return invalid
		}
	default:
		return invalid
	}
	return nil
}

// TechnicalIdentifier rejects arbitrary prose, paths with spaces and control bytes.
// Callers must also use an allowlisted semantic source (model/tool/version only).
func TechnicalIdentifier(value string) string {
	if len(value) == 0 || len(value) > 128 {
		return ""
	}
	for _, c := range value {
		if (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || strings.ContainsRune("._-:/@+", c) {
			continue
		}
		return ""
	}
	return value
}
