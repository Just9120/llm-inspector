package domain

type RequestResourceContext struct{ RequestID, OperationID, BackendURL string }

// ResourceSession methods on the relay path must be nonblocking. Actual probing
// and terminal persistence belong to the monitor's background workers.
type ResourceSession interface {
	StageChanged(StageValue)
	AddSent(int)
	AddReceived(int)
	Complete()
}

// ResourceRuntimeEvidence returns already captured request-scoped metadata.
// It must not probe, wait for a capture or expose paths/host identifiers.
type ResourceRuntimeEvidence interface {
	GPUDriverVersion() string
}
type ResourceMonitor interface {
	Start(RequestResourceContext) ResourceSession
}
