package gateway

import (
	"errors"
	"net"
	"syscall"
)

func transportError(err error, responseStarted bool) string {
	if errors.Is(err, syscall.ECONNREFUSED) {
		return "connection_refused"
	}
	var ne net.Error
	if errors.As(err, &ne) && ne.Timeout() {
		return "timeout"
	}
	if responseStarted || errors.Is(err, syscall.ECONNRESET) || errors.Is(err, syscall.ECONNABORTED) {
		return "backend_crash"
	}
	return "backend_unavailable"
}
func httpError(status int, overflow bool) string {
	switch {
	case status < 400:
		return "none"
	case status == 503:
		return "model_loading"
	case status == 408 || status == 504:
		return "timeout"
	case status == 413 || overflow:
		return "context_overflow"
	default:
		return "http_api_error"
	}
}
func errorOrigin(kind string) string {
	switch kind {
	case "none":
		return "not_applicable"
	case "client_cancellation":
		return "client"
	case "model_loading", "context_overflow":
		return "model"
	case "inspector_failure":
		return "inspector"
	case "relay_failure":
		return "unknown"
	case "connection_refused", "http_api_error", "timeout", "backend_crash", "backend_unavailable":
		return "backend"
	default:
		return "unknown"
	}
}
