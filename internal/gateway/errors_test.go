package gateway

import (
	"errors"
	"fmt"
	"io"
	"net"
	"syscall"
	"testing"
)

func TestTypedTransportErrorsWithoutMessageGuessing(t *testing.T) {
	for _, tc := range []struct {
		err     error
		started bool
		want    string
	}{
		{fmt.Errorf("wrapped: %w", syscall.ECONNREFUSED), false, "connection_refused"},
		{&net.DNSError{IsTimeout: true}, false, "timeout"},
		{syscall.ECONNRESET, false, "backend_crash"},
		{io.ErrUnexpectedEOF, true, "backend_crash"},
		{errors.New("connection refused PRIVATE_ERROR"), false, "backend_unavailable"},
	} {
		if got := transportError(tc.err, tc.started); got != tc.want {
			t.Fatalf("wrong typed classification: %s", got)
		}
	}
	if errorOrigin("unknown") != "unknown" || errorOrigin("inspector_failure") != "inspector" || errorOrigin("relay_failure") != "unknown" {
		t.Fatal("error origin guessed")
	}
}
