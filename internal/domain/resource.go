package domain

import "time"

type ProcessAssociation struct {
	PID           int       `json:"pid"`
	StartedAt     time.Time `json:"started_at"`
	ImageName     string    `json:"image_name"`
	SourceVersion string    `json:"source_version"`
}

func (p *ProcessAssociation) Valid() bool {
	return p != nil && p.PID > 0 && !p.StartedAt.IsZero() && TechnicalIdentifier(p.ImageName) != "" && TechnicalIdentifier(p.SourceVersion) != ""
}

type ResourceSample struct {
	ID               string              `json:"id"`
	RequestID        string              `json:"request_id,omitempty"`
	OperationID      string              `json:"operation_id,omitempty"`
	CapturedAt       time.Time           `json:"captured_at"`
	Stage            *StageValue         `json:"stage,omitempty"`
	Process          *ProcessAssociation `json:"process,omitempty"`
	GPUDeviceID      string              `json:"gpu_device_id,omitempty"`
	GPUDriverVersion string              `json:"gpu_driver_version,omitempty"`
	DroppedSamples   int                 `json:"dropped_samples"`
	CPU              Metric              `json:"cpu"`
	MemoryPercent    Metric              `json:"memory_percent"`
	MemoryUsed       Metric              `json:"memory_used"`
	ProcessCPU       Metric              `json:"process_cpu"`
	ProcessMemory    Metric              `json:"process_memory"`
	DiskRead         Metric              `json:"disk_read"`
	DiskWrite        Metric              `json:"disk_write"`
	ClientToBackend  Metric              `json:"client_to_backend"`
	BackendToClient  Metric              `json:"backend_to_client"`
	GPUUtilization   Metric              `json:"gpu_utilization"`
	GPUVRAMUsed      Metric              `json:"gpu_vram_used"`
	GPUVRAMTotal     Metric              `json:"gpu_vram_total"`
	GPUTemperature   Metric              `json:"gpu_temperature"`
	GPUPower         Metric              `json:"gpu_power"`
}

func MissingResource() ResourceSample {
	m := func(unit Unit) Metric { return Missing(unit, "inspector", "resource-monitor-unavailable-v1") }
	return ResourceSample{CPU: m(Percent), MemoryPercent: m(Percent), MemoryUsed: m(Bytes), ProcessCPU: m(Percent), ProcessMemory: m(Bytes), DiskRead: m(Bytes), DiskWrite: m(Bytes), ClientToBackend: m(Bytes), BackendToClient: m(Bytes), GPUUtilization: m(Percent), GPUVRAMUsed: m(Bytes), GPUVRAMTotal: m(Bytes), GPUTemperature: m(Celsius), GPUPower: m(Watts)}
}
