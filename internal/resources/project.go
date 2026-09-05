package resources

import (
	"crypto/rand"
	"encoding/hex"
	"math"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type RequestContext = domain.RequestResourceContext
type Traffic struct{ Sent, Received uint64 }

// Project emits each host/process counter only in the first GPU row, preventing
// multi-GPU hosts from multiplying CPU/RAM/traffic aggregate sample counts.
func Project(request RequestContext, stage domain.StageValue, association *domain.ProcessAssociation, previous, current *Snapshot, traffic Traffic, local bool, processors int, at time.Time) []domain.ResourceSample {
	if !local {
		previous = nil
		current = nil
		association = nil
	}
	count := 1
	if current != nil && len(current.GPUs) > 0 {
		count = min(len(current.GPUs), 16)
	}
	result := make([]domain.ResourceSample, 0, count)
	for i := 0; i < count; i++ {
		r := domain.MissingResource()
		var rid [16]byte
		if _, err := rand.Read(rid[:]); err != nil {
			return nil
		}
		r.ID = hex.EncodeToString(rid[:])
		r.RequestID = request.RequestID
		r.OperationID = request.OperationID
		r.CapturedAt = at.UTC()
		if stage.Valid() {
			v := stage
			r.Stage = &v
		}
		source, version := "windows_api", WindowsVersion
		if !local {
			source, version = "inspector", "remote-backend-resource-unavailable-v1"
		}
		missing := func(unit domain.Unit) domain.Metric { return domain.Missing(unit, source, version) }
		r.CPU = missing(domain.Percent)
		r.MemoryPercent = missing(domain.Percent)
		r.MemoryUsed = missing(domain.Bytes)
		r.ProcessCPU = missing(domain.Percent)
		r.ProcessMemory = missing(domain.Bytes)
		r.DiskRead = missing(domain.Bytes)
		r.DiskWrite = missing(domain.Bytes)
		r.GPUUtilization = domain.Missing(domain.Percent, "nvidia_smi", NvidiaVersion)
		r.GPUVRAMUsed = domain.Missing(domain.Bytes, "nvidia_smi", NvidiaVersion)
		r.GPUVRAMTotal = domain.Missing(domain.Bytes, "nvidia_smi", NvidiaVersion)
		r.GPUTemperature = domain.Missing(domain.Celsius, "nvidia_smi", NvidiaVersion)
		r.GPUPower = domain.Missing(domain.Watts, "nvidia_smi", NvidiaVersion)
		r.ClientToBackend = domain.Missing(domain.Bytes, "gateway_traffic", TrafficVersion)
		r.BackendToClient = domain.Missing(domain.Bytes, "gateway_traffic", TrafficVersion)
		if i == 0 {
			r.ClientToBackend = domain.Measured(float64(traffic.Sent), domain.Bytes, "gateway_traffic", TrafficVersion)
			r.BackendToClient = domain.Measured(float64(traffic.Received), domain.Bytes, "gateway_traffic", TrafficVersion)
			if local && current != nil {
				if current.MemoryAvailable && current.TotalMemory > 0 && current.AvailableMemory <= current.TotalMemory {
					used := current.TotalMemory - current.AvailableMemory
					r.MemoryUsed = derived(float64(used), domain.Bytes, "windows_api", WindowsVersion, "total-minus-available-v1")
					r.MemoryPercent = derived(100*float64(used)/float64(current.TotalMemory), domain.Percent, "windows_api", WindowsVersion, "physical-memory-ratio-v1")
				}
				if association.Valid() && current.Process != nil {
					p := *association
					r.Process = &p
					r.ProcessMemory = domain.Measured(float64(current.Process.WorkingSet), domain.Bytes, "windows_api", WindowsVersion)
				}
				if previous != nil {
					if previous.CPUAvailable && current.CPUAvailable {
						r.CPU = systemCPU(*previous, *current)
					}
					if r.Process != nil && previous.Process != nil {
						p, c := previous.Process, current.Process
						wall := current.CapturedAt.Sub(previous.CapturedAt)
						if wall > 0 && c.CPU100ns >= p.CPU100ns && processors > 0 {
							v := 100 * float64(c.CPU100ns-p.CPU100ns) * 100 / float64(wall.Nanoseconds()) / float64(processors)
							r.ProcessCPU = derived(math.Min(100, v), domain.Percent, "windows_api", WindowsVersion, "process-time-wall-delta-v1")
						}
						r.DiskRead = counter(p.ReadBytes, c.ReadBytes)
						r.DiskWrite = counter(p.WriteBytes, c.WriteBytes)
					}
				}
			}
		}
		if local && current != nil && len(current.GPUs) > i {
			gpu := current.GPUs[i]
			r.GPUDeviceID = domain.TechnicalIdentifier(gpu.ID)
			r.GPUDriverVersion = domain.TechnicalIdentifier(gpu.Driver)
			r.GPUUtilization = optional(gpu.Utilization, domain.Percent)
			r.GPUVRAMUsed = mib(gpu.UsedMiB)
			r.GPUVRAMTotal = mib(gpu.TotalMiB)
			r.GPUTemperature = optional(gpu.Temperature, domain.Celsius)
			r.GPUPower = optional(gpu.Power, domain.Watts)
		}
		result = append(result, r)
	}
	return result
}
func derived(v float64, unit domain.Unit, source, version, derivation string) domain.Metric {
	m := domain.Metric{Value: &v, Unit: unit, Quality: domain.Calculated, Source: source, SourceVersion: version, DerivationVersion: derivation}
	if m.Validate() != nil {
		return domain.Missing(unit, source, version)
	}
	return m
}
func systemCPU(p, c Snapshot) domain.Metric {
	m := domain.Missing(domain.Percent, "windows_api", WindowsVersion)
	if c.Kernel < p.Kernel || c.User < p.User || c.Idle < p.Idle {
		return m
	}
	k, u := c.Kernel-p.Kernel, c.User-p.User
	if k > math.MaxUint64-u {
		return m
	}
	total := k + u
	idle := c.Idle - p.Idle
	if total == 0 || idle > total {
		return m
	}
	return derived(100*float64(total-idle)/float64(total), domain.Percent, "windows_api", WindowsVersion, "system-time-delta-v1")
}
func counter(p, c uint64) domain.Metric {
	if c < p {
		return domain.Missing(domain.Bytes, "windows_api", WindowsVersion)
	}
	return derived(float64(c-p), domain.Bytes, "windows_api", WindowsVersion, "cumulative-counter-delta-v1")
}
func optional(v *float64, u domain.Unit) domain.Metric {
	if v == nil {
		return domain.Missing(u, "nvidia_smi", NvidiaVersion)
	}
	return domain.Measured(*v, u, "nvidia_smi", NvidiaVersion)
}
func mib(v *float64) domain.Metric {
	if v == nil {
		return domain.Missing(domain.Bytes, "nvidia_smi", NvidiaVersion)
	}
	return derived(*v*1048576, domain.Bytes, "nvidia_smi", NvidiaVersion, "mebibytes-to-bytes-v1")
}
