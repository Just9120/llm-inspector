// Package performance implements the agreed budgets, not measured readiness.
package performance

import (
	"errors"
	"time"
)

const ContractVersion = "performance-profiles-v1"

type ProfileID string

const (
	Saver    ProfileID = "saver"
	Balanced ProfileID = "balanced"
	Detailed ProfileID = "detailed"
	Custom   ProfileID = "custom"
)

type Profile struct {
	ID                   ProfileID          `json:"id"`
	Name                 string             `json:"name"`
	SamplingMilliseconds int                `json:"sampling_milliseconds"`
	ReleaseBudget        map[string]float64 `json:"release_budget"`
}

func (p Profile) Interval() time.Duration {
	return time.Duration(p.SamplingMilliseconds) * time.Millisecond
}

var metricNames = [16]string{"active_cpu_mean_pp", "active_cpu_p95_pp", "process_private_bytes_p95", "active_ram_growth_30m", "gpu_utilization_delta_mean_pp", "gpu_utilization_delta_p95_pp", "dedicated_vram_p95", "disk_writes_per_minute", "throughput_regression_median_percent", "throughput_regression_p95_percent", "idle_cpu_mean_percent", "idle_cpu_p95_percent", "idle_ram_growth_per_hour", "idle_disk_writes_per_hour", "idle_wakeups_mean_per_second", "idle_wakeups_p95_per_second"}

const mib = 1048576

func Resolve(id ProfileID, customMilliseconds int) (Profile, error) {
	p := Profile{ID: id}
	var limits [16]float64
	switch id {
	case Saver:
		p.Name = "Бережный"
		p.SamplingMilliseconds = 2000
		limits = [16]float64{1.5, 4, 192 * mib, 16 * mib, 1, 3, 128 * mib, mib, 3, 5, .25, 1, 8 * mib, mib / 4, 2, 8}
	case Balanced:
		p.Name = "Сбалансированный"
		p.SamplingMilliseconds = 1000
		limits = [16]float64{3, 8, 256 * mib, 32 * mib, 2, 5, 192 * mib, 2 * mib, 5, 10, .5, 2, 16 * mib, mib, 5, 15}
	case Detailed:
		p.Name = "Детальный"
		p.SamplingMilliseconds = 500
		limits = [16]float64{5, 12, 384 * mib, 64 * mib, 3, 8, 256 * mib, 5 * mib, 8, 15, 1, 4, 32 * mib, 5 * mib, 15, 30}
	case Custom:
		if customMilliseconds < 250 || customMilliseconds > 10000 {
			return Profile{}, errors.New("интервал своего профиля должен быть от 250 до 10000 мс")
		}
		p.Name = "Свой профиль"
		p.SamplingMilliseconds = customMilliseconds
		return p, nil
	default:
		return Profile{}, errors.New("неизвестный профиль мониторинга")
	}
	p.ReleaseBudget = make(map[string]float64, len(metricNames))
	for i, k := range metricNames {
		p.ReleaseBudget[k] = limits[i]
	}
	return p, nil
}
func BuiltIn() []Profile {
	result := make([]Profile, 0, 3)
	for _, id := range []ProfileID{Saver, Balanced, Detailed} {
		p, _ := Resolve(id, 1000)
		result = append(result, p)
	}
	return result
}
