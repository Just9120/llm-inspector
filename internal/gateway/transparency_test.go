package gateway

import (
	"bytes"
	"compress/gzip"
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestEncodedAndMalformedBodiesPassThroughWithoutFabricatedTelemetry(t *testing.T) {
	var compressed bytes.Buffer
	z := gzip.NewWriter(&compressed)
	_, _ = io.WriteString(z, `{"usage":{"prompt_tokens":123},"content":"PRIVATE"}`)
	_ = z.Close()
	for _, tc := range []struct {
		body     []byte
		encoding string
	}{{compressed.Bytes(), "gzip"}, {[]byte(`{"usage":{"prompt_tokens":123},}`), ""}} {
		t.Run(tc.encoding, func(t *testing.T) {
			backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				_, _ = io.Copy(io.Discard, r.Body)
				w.Header().Set("Content-Type", "application/json")
				if tc.encoding != "" {
					w.Header().Set("Content-Encoding", tc.encoding)
				}
				_, _ = w.Write(tc.body)
			}))
			defer backend.Close()
			sink := make(chan domain.Observation, 1)
			base := startTestGateway(t, backend, sink)
			client := &http.Client{Transport: &http.Transport{DisableCompression: true}, Timeout: 3 * time.Second}
			defer client.CloseIdleConnections()
			resp, err := client.Post(base+"/v1/chat/completions", "application/json", strings.NewReader(`{}`))
			if err != nil {
				t.Fatal(err)
			}
			body, err := io.ReadAll(resp.Body)
			resp.Body.Close()
			if err != nil || !bytes.Equal(body, tc.body) {
				t.Fatal("relay changed body")
			}
			select {
			case obs := <-sink:
				if obs.Telemetry.PromptTokens.Value != nil || obs.TTFT.Value != nil {
					t.Fatal("unproven metric")
				}
			case <-time.After(time.Second):
				t.Fatal("missing observation")
			}
		})
	}
}

func TestStreamingRequestAndResponseDoNotWaitForCompletion(t *testing.T) {
	seen := make(chan struct{})
	release := make(chan struct{})
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b := make([]byte, 3)
		if _, err := io.ReadFull(r.Body, b); err != nil {
			return
		}
		close(seen)
		_, _ = io.Copy(io.Discard, r.Body)
		w.Header().Set("Content-Type", "text/event-stream")
		_, _ = io.WriteString(w, "data: {}\n\n")
		w.(http.Flusher).Flush()
		select {
		case <-release:
		case <-r.Context().Done():
		}
	}))
	defer backend.Close()
	defer close(release)
	base := startTestGateway(t, backend, make(chan domain.Observation, 1))
	reader, writer := io.Pipe()
	defer writer.Close()
	defer reader.Close()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	req, _ := http.NewRequestWithContext(ctx, "POST", base+"/v1/chat/completions", reader)
	response := make(chan *http.Response, 1)
	go func() { r, _ := http.DefaultClient.Do(req); response <- r }()
	_, _ = writer.Write([]byte("{\"x"))
	select {
	case <-seen:
	case <-ctx.Done():
		t.Fatal("request buffered")
	}
	_, _ = writer.Write([]byte("\":1}"))
	_ = writer.Close()
	select {
	case resp := <-response:
		if resp == nil {
			t.Fatal("missing streaming response")
		}
		defer resp.Body.Close()
		first := make([]byte, len("data: {}\n\n"))
		if _, err := io.ReadFull(resp.Body, first); err != nil {
			t.Fatal("first event was buffered")
		}
	case <-ctx.Done():
		t.Fatal("response buffered")
	}
}

func TestConcurrentRequestsRemainIsolatedAndRestartIsSafe(t *testing.T) {
	var requests atomic.Int32
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, _ = io.Copy(io.Discard, r.Body)
		requests.Add(1)
		w.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(w, `{"usage":{"prompt_tokens":3,"completion_tokens":4}}`)
	}))
	defer backend.Close()
	sink := make(chan domain.Observation, 32)
	// Do not leave speculative idle dials in the shared default transport while
	// asserting a short graceful shutdown deadline on a freshly reused listener.
	client := &http.Client{Transport: &http.Transport{DisableKeepAlives: true}, Timeout: 5 * time.Second}
	defer client.CloseIdleConnections()
	g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL}, sink, true)
	if err != nil {
		t.Fatal(err)
	}
	for iteration := 0; iteration < 3; iteration++ {
		base, err := g.Start()
		if err != nil {
			t.Fatal(err)
		}
		var wg sync.WaitGroup
		for i := 0; i < 16; i++ {
			wg.Go(func() {
				resp, err := client.Post(base+"/v1/chat/completions", "application/json", strings.NewReader(`{}`))
				if err != nil {
					t.Error(err)
					return
				}
				_, _ = io.Copy(io.Discard, resp.Body)
				resp.Body.Close()
			})
		}
		wg.Wait()
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		err = g.Stop(ctx)
		cancel()
		if err != nil {
			t.Fatal(err)
		}
		ids := map[string]bool{}
		for i := 0; i < 16; i++ {
			select {
			case obs := <-sink:
				if ids[obs.RequestID] || obs.Telemetry.TotalTokens.Value == nil || *obs.Telemetry.TotalTokens.Value != 7 {
					t.Fatal("cross-request state")
				}
				ids[obs.RequestID] = true
			case <-time.After(time.Second):
				t.Fatal("missing observation")
			}
		}
	}
	if requests.Load() != 48 || g.Dropped() != 0 {
		t.Fatal("retry or dropped request")
	}
}

func TestResponseTrailersAndAbsentUserAgentArePreserved(t *testing.T) {
	gotAgent := make(chan bool, 1)
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		_, exists := r.Header["User-Agent"]
		gotAgent <- exists
		w.Header().Set("Trailer", "X-Technical-Checksum")
		_, _ = io.WriteString(w, "[]")
		w.Header().Set("X-Technical-Checksum", "fixture-digest")
	}))
	defer backend.Close()
	base := startTestGateway(t, backend, make(chan domain.Observation, 1))
	req, _ := http.NewRequest("GET", base+"/v1/models", nil)
	req.Header["User-Agent"] = nil
	resp, err := http.DefaultClient.Do(req)
	if err != nil {
		t.Fatal(err)
	}
	_, err = io.Copy(io.Discard, resp.Body)
	resp.Body.Close()
	if err != nil || resp.Trailer.Get("X-Technical-Checksum") != "fixture-digest" || <-gotAgent {
		t.Fatal("header/trailer semantics changed")
	}
}
