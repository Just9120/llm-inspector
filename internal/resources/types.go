// Package resources isolates optional Windows/driver collection from forwarding.
package resources

import (
	"context"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type ProcessSnapshot struct {
	CPU100ns   uint64
	WorkingSet uint64
	ReadBytes  uint64
	WriteBytes uint64
}
type GPU struct {
	Index                                              int
	ID, Driver                                         string
	Utilization, UsedMiB, TotalMiB, Temperature, Power *float64
}
type Snapshot struct {
	CapturedAt                   time.Time
	Idle, Kernel, User           uint64
	CPUAvailable                 bool
	TotalMemory, AvailableMemory uint64
	MemoryAvailable              bool
	Process                      *ProcessSnapshot
	GPUs                         []GPU
}
type Probe interface {
	Capture(context.Context, *domain.ProcessAssociation) (Snapshot, error)
}
type Resolver interface {
	Resolve(string) *domain.ProcessAssociation
}

const WindowsVersion = "windows-resource-api-v1"
const NvidiaVersion = "nvidia-smi-query-v1"
const TrafficVersion = "gateway-relayed-byte-counter-v1"
