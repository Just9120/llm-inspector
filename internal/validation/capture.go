// Package validation contains offline/read-only validation tools. It is not
// imported by the production desktop and cannot certify a release by itself.
package validation

import (
	"context"
	"errors"
	"math"
	"slices"
	"strings"
	"time"

	"github.com/Just9120/llm-inspector/internal/performance"
)

type Counters struct {
	CPU100ns     uint64 `json:"cpu_100ns"`
	PrivateBytes uint64 `json:"private_bytes"`
	IOWriteBytes uint64 `json:"io_write_bytes"`
	Live         int    `json:"live_processes"`
	Retained     int    `json:"retained_processes"`
}

type Sample struct {
	ElapsedSeconds float64   `json:"elapsed_seconds"`
	CapturedUTC    time.Time `json:"captured_utc"`
	ProbeSeconds   float64   `json:"probe_seconds"`
	Counters       *Counters `json:"counters"`
	ErrorCode      string    `json:"error_code,omitempty"`
}

type Summary struct {
	CPUCapacityMeanPercent *float64 `json:"observed_tree_cpu_capacity_mean_percent"`
	CPUCapacityP95Percent  *float64 `json:"observed_tree_cpu_capacity_p95_percent"`
	PrivateBytesP95        *float64 `json:"observed_tree_private_bytes_p95"`
	PrivateGrowthBytes     *float64 `json:"observed_tree_private_growth_bytes"`
	IOWriteBytes           *float64 `json:"observed_tree_io_write_bytes"`
	MeasuredSeconds        float64  `json:"measured_seconds"`
	Complete               bool     `json:"observed_samples_valid"`
}

type Options struct {
	SourceSHA     string
	ExecutableSHA string
	Profile       performance.ProfileID
	Phase         string
	Duration      time.Duration
	Interval      time.Duration
	LogicalCPUs   int
}

func hexID(value string, length int) bool {
	return len(value) == length && strings.IndexFunc(value, func(c rune) bool {
		return !(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')
	}) < 0
}

func (o Options) Validate() error {
	p, err := performance.Resolve(o.Profile, 1000)
	if err != nil || p.ReleaseBudget == nil || !hexID(o.SourceSHA, 40) || !hexID(o.ExecutableSHA, 64) {
		return errors.New("invalid_identity_or_profile")
	}
	if o.Phase != "pilot" && o.Phase != "idle" && o.Phase != "active" {
		return errors.New("invalid_phase")
	}
	if o.LogicalCPUs < 1 || o.LogicalCPUs > 4096 || o.Interval < 200*time.Millisecond || o.Interval > 10*time.Second || o.Duration < o.Interval || o.Duration > 2*time.Hour || o.Duration/o.Interval > 36000 {
		return errors.New("invalid_capture_bounds")
	}
	return nil
}

type Report struct {
	SchemaVersion   string                `json:"schema_version"`
	SourceSHA       string                `json:"declared_source_sha"`
	ExecutableSHA   string                `json:"verified_executable_sha256"`
	Profile         performance.ProfileID `json:"declared_profile"`
	Phase           string                `json:"declared_phase"`
	LogicalCPUs     int                   `json:"logical_cpu_count"`
	DurationSeconds float64               `json:"requested_duration_seconds"`
	IntervalSeconds float64               `json:"requested_interval_seconds"`
	StartedUTC      time.Time             `json:"started_utc"`
	EndedUTC        time.Time             `json:"ended_utc"`
	TerminalStatus  string                `json:"terminal_status"`
	ReleaseEvidence bool                  `json:"release_evidence"`
	Limitations     []string              `json:"limitations"`
	Samples         []Sample              `json:"samples"`
	Summary         Summary               `json:"summary"`
}

// CounterSource must not mutate/stop the target. It is called serially.
type CounterSource interface{ Read() (Counters, error) }

func Capture(ctx context.Context, o Options, source CounterSource) (Report, error) {
	if err := o.Validate(); err != nil {
		return Report{}, err
	}
	if source == nil {
		return Report{}, errors.New("missing_counter_source")
	}
	r := Report{
		SchemaVersion: "windows-process-capture-v1", SourceSHA: o.SourceSHA, ExecutableSHA: o.ExecutableSHA,
		Profile: o.Profile, Phase: o.Phase, LogicalCPUs: o.LogicalCPUs,
		DurationSeconds: o.Duration.Seconds(), IntervalSeconds: o.Interval.Seconds(),
		TerminalStatus: "completed", Samples: []Sample{},
		Limitations: []string{
			"Supporting process-counter capture, not an E12 PASS or release certificate.",
			"Source revision, profile, phase, warm-up and foreign-load control require independent evidence.",
			"Snapshot-discovered descendants only: children born and exited between snapshots may be missed; event tracing is required for complete tree coverage.",
			"IOWriteBytes is generic process I/O, NOT a physical-disk-write metric or an idle wakeup count.",
			"No GPU/VRAM, wakeups, disk-only attribution, paired throughput or contamination verification is supplied.",
			"Sampling is best effort: slow probes/scheduling may skip ticks; actual timestamps and probe duration must be reviewed separately.",
			"Memory is private committed bytes, NOT working set. CPU mean is normalized to total logical capacity; P95 is nearest-rank over observed intervals.",
		},
	}
	start := time.Now()
	r.StartedUTC = start.UTC()
	take := func() bool {
		before := time.Now()
		counters, err := source.Read()
		after := time.Now()
		s := Sample{ElapsedSeconds: after.Sub(start).Seconds(), CapturedUTC: after.UTC(), ProbeSeconds: after.Sub(before).Seconds()}
		if err != nil {
			s.ErrorCode = "counter_source_failed"
			r.TerminalStatus = "counter_source_failed"
		} else {
			s.Counters = &counters
		}
		r.Samples = append(r.Samples, s)
		return err == nil
	}
	if ctx.Err() != nil {
		r.TerminalStatus = "cancelled"
	} else if take() {
		ticker := time.NewTicker(o.Interval)
		defer ticker.Stop()
		for time.Since(start) < o.Duration {
			select {
			case <-ctx.Done():
				r.TerminalStatus = "cancelled"
			case <-ticker.C:
				if !take() {
					break
				}
			}
			if r.TerminalStatus != "completed" {
				break
			}
		}
	}
	r.EndedUTC = time.Now().UTC()
	r.Summary = Summarize(r.Samples, o.LogicalCPUs)
	if r.TerminalStatus != "completed" {
		r.Summary.Complete = false
	}
	return r, nil
}

func Summarize(samples []Sample, cpus int) Summary {
	invalid := Summary{}
	if len(samples) < 2 || cpus < 1 {
		return invalid
	}
	private, rates := []float64{}, []float64{}
	for i, s := range samples {
		if s.Counters == nil || s.ErrorCode != "" || !finite(s.ElapsedSeconds) || s.ElapsedSeconds < 0 || s.Counters.Live < 1 || s.Counters.Retained < s.Counters.Live {
			return invalid
		}
		private = append(private, float64(s.Counters.PrivateBytes))
		if i == 0 {
			continue
		}
		prev := samples[i-1]
		dt := s.ElapsedSeconds - prev.ElapsedSeconds
		if dt <= 0 || s.Counters.CPU100ns < prev.Counters.CPU100ns || s.Counters.IOWriteBytes < prev.Counters.IOWriteBytes {
			return invalid
		}
		rate := float64(s.Counters.CPU100ns-prev.Counters.CPU100ns) / 1e7 / dt / float64(cpus) * 100
		if !finite(rate) || rate > 100.1 {
			return invalid
		}
		rates = append(rates, rate)
	}
	first, last := samples[0], samples[len(samples)-1]
	elapsed := last.ElapsedSeconds - first.ElapsedSeconds
	mean := float64(last.Counters.CPU100ns-first.Counters.CPU100ns) / 1e7 / elapsed / float64(cpus) * 100
	growth := float64(last.Counters.PrivateBytes) - float64(first.Counters.PrivateBytes)
	writes := float64(last.Counters.IOWriteBytes - first.Counters.IOWriteBytes)
	p95CPU, p95Memory := p95(rates), p95(private)
	return Summary{&mean, &p95CPU, &p95Memory, &growth, &writes, elapsed, true}
}

func finite(v float64) bool { return !math.IsNaN(v) && !math.IsInf(v, 0) }
func p95(values []float64) float64 {
	copy := slices.Clone(values)
	slices.Sort(copy)
	return copy[int(math.Ceil(float64(len(copy))*.95))-1]
}
