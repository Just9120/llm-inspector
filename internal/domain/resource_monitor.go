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
type ResourceMonitor interface {
	Start(RequestResourceContext) ResourceSession
}
