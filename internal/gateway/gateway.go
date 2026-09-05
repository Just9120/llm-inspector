package gateway

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"errors"
	"io"
	"log"
	"net"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/telemetry"
)

type Gateway struct {
	config    Config
	target    *url.URL
	transport *http.Transport
	sink      chan<- domain.Observation
	mu        sync.Mutex
	server    *http.Server
	listener  net.Listener
	dropped   atomic.Uint64
}

func New(c Config, sink chan<- domain.Observation) (*Gateway, error) {
	return newGateway(c, sink, false)
}

func newGateway(c Config, sink chan<- domain.Observation, test bool) (*Gateway, error) {
	target, err := c.target(test)
	if err != nil {
		return nil, err
	}
	// No environment proxy, decompression, redirects, cookies or automatic retries.
	tr := &http.Transport{Proxy: nil, DisableCompression: true, DisableKeepAlives: true, ForceAttemptHTTP2: false, DialContext: (&net.Dialer{Timeout: 10 * time.Second}).DialContext, TLSHandshakeTimeout: 10 * time.Second, MaxResponseHeaderBytes: 1 << 20}
	return &Gateway{config: c, target: target, transport: tr, sink: sink}, nil
}

func (g *Gateway) Start() (string, error) {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.listener != nil {
		return "", errors.New("proxy уже запущен")
	}
	l, err := net.Listen("tcp4", net.JoinHostPort("127.0.0.1", strconv.Itoa(g.config.Port)))
	if err != nil {
		return "", errors.New("не удалось открыть локальный порт proxy")
	}
	g.listener = l
	g.server = &http.Server{Handler: g, ReadHeaderTimeout: 10 * time.Second, MaxHeaderBytes: 1 << 20, ErrorLog: log.New(io.Discard, "", 0)}
	server := g.server
	go func() { _ = server.Serve(l) }()
	return "http://" + l.Addr().String(), nil
}

func (g *Gateway) Stop(ctx context.Context) error {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.server == nil {
		return nil
	}
	err := g.server.Shutdown(ctx)
	if err != nil {
		_ = g.server.Close()
	}
	g.transport.CloseIdleConnections()
	g.listener = nil
	g.server = nil
	return err
}

func (g *Gateway) Dropped() uint64 { return g.dropped.Load() }

func route(path, method string, backend domain.Backend) (string, domain.Client, bool) {
	client := domain.Generic
	for _, c := range []domain.Client{domain.OpenCode, domain.Hermes, domain.Cline, domain.OpenWebUI} {
		prefix := "/clients/" + string(c)
		if strings.HasPrefix(path, prefix+"/") {
			client = c
			path = strings.TrimPrefix(path, prefix)
			break
		}
	}
	if path == "/v1/models" && method == http.MethodGet {
		return path, client, true
	}
	if path == "/v1/chat/completions" && method == http.MethodPost {
		return path, client, true
	}
	if path == "/api/v1/chat" && method == http.MethodPost && backend == domain.LMStudio && client == domain.Generic {
		return path, client, true
	}
	return "", client, false
}

var identityHeaders = []string{"Tailscale-User-Login", "Tailscale-User-Name", "Tailscale-User-Profile-Pic", "Tailscale-App-Capabilities", "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto"}
var correlationHeaders = []string{"X-LLM-Inspector-Session-Id", "X-LLM-Inspector-Turn-Id", "X-LLM-Inspector-Turn-Sequence", "X-LLM-Inspector-Operation-Id"}

func localIngress(r *http.Request) bool {
	host := r.Host
	if h, _, err := net.SplitHostPort(host); err == nil {
		host = h
	}
	if !strings.EqualFold(host, "localhost") {
		ip := net.ParseIP(strings.Trim(host, "[]"))
		if ip == nil || !ip.IsLoopback() {
			return false
		}
	}
	for _, key := range identityHeaders {
		if len(r.Header.Values(key)) > 0 {
			return false
		}
	}
	return true
}

func scrubHopHeaders(h http.Header) {
	for _, line := range h.Values("Connection") {
		for _, key := range strings.Split(line, ",") {
			h.Del(strings.TrimSpace(key))
		}
	}
	for _, key := range []string{"Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization", "Te", "Trailer", "Transfer-Encoding", "Upgrade"} {
		h.Del(key)
	}
}

func validGUID(v string) bool {
	if len(v) != 32 || v == strings.Repeat("0", 32) {
		return false
	}
	_, err := hex.DecodeString(v)
	return err == nil
}

func readCorrelation(h http.Header) *domain.Correlation {
	for _, k := range correlationHeaders[:3] {
		if len(h.Values(k)) != 1 {
			return nil
		}
	}
	s, t, n := h.Get(correlationHeaders[0]), h.Get(correlationHeaders[1]), h.Get(correlationHeaders[2])
	seq, err := strconv.Atoi(n)
	if err != nil || seq < 1 || seq > 2147483647 || !validGUID(s) || !validGUID(t) {
		return nil
	}
	c := &domain.Correlation{SessionID: strings.ToLower(s), TurnID: strings.ToLower(t), Sequence: seq}
	if v := h.Get(correlationHeaders[3]); len(h.Values(correlationHeaders[3])) == 1 && validGUID(v) {
		c.OperationID = strings.ToLower(v)
	}
	return c
}

func safeResponse(w http.ResponseWriter, status int, code string) {
	w.Header().Set("Content-Type", "application/problem+json")
	w.WriteHeader(status)
	_, _ = io.WriteString(w, `{"error":{"type":"`+code+`"}}`)
}

func (g *Gateway) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if !localIngress(r) {
		safeResponse(w, 403, "remote_access_disabled")
		return
	}
	path, client, ok := route(r.URL.Path, r.Method, g.config.Backend)
	if !ok || r.URL.RawPath != "" {
		safeResponse(w, 404, "route_not_found")
		return
	}
	isChat := r.Method == http.MethodPost
	start := time.Now()
	id := make([]byte, 16)
	if _, err := rand.Read(id); err != nil {
		safeResponse(w, 500, "inspector_unavailable")
		return
	}
	obs := domain.Observation{RequestID: hex.EncodeToString(id), StartedAt: start.UTC(), Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: client, Telemetry: domain.MissingTelemetry(g.config.Backend), TTFT: domain.Missing(domain.Milliseconds, "inspector", "streaming-ttft-v1"), ContextChange: domain.Missing(domain.TokenDelta, "inspector", "correlation-v1"), Correlation: readCorrelation(r.Header), Agent: domain.MissingAgentTurn()}
	if isChat {
		defer func() {
			obs.DurationMS = float64(time.Since(start)) / float64(time.Millisecond)
			select {
			case g.sink <- obs:
			default:
				g.dropped.Add(1)
			}
		}()
	}
	out := r.Clone(r.Context())
	dest := *g.target
	dest.Path = path
	dest.RawQuery = r.URL.RawQuery
	out.URL = &dest
	out.RequestURI = ""
	out.Host = dest.Host
	out.Header = r.Header.Clone()
	if _, exists := out.Header["User-Agent"]; !exists {
		// The transport must not invent a Go client attribution header.
		out.Header["User-Agent"] = nil
	}
	out.GetBody = nil
	scrubHopHeaders(out.Header)
	for _, k := range append(append([]string{}, identityHeaders...), correlationHeaders...) {
		out.Header.Del(k)
	}
	resp, err := g.transport.RoundTrip(out)
	if err != nil {
		obs.Outcome = "backend_unavailable"
		obs.ErrorType = "connection_refused"
		obs.ErrorOrigin = "backend"
		if r.Context().Err() != nil {
			obs.Outcome = "client_cancelled"
			obs.ErrorType = "client_cancellation"
			obs.ErrorOrigin = "client"
		} else {
			var ne net.Error
			if errors.As(err, &ne) && ne.Timeout() {
				obs.ErrorType = "timeout"
			}
		}
		safeResponse(w, 502, "backend_unavailable")
		return
	}
	defer resp.Body.Close()
	obs.HTTPStatus = &resp.StatusCode
	if resp.StatusCode >= 400 {
		obs.ErrorType = "http_api_error"
		obs.ErrorOrigin = "backend"
		if resp.StatusCode == 503 {
			obs.ErrorType = "model_loading"
		}
	}
	scrubHopHeaders(resp.Header)
	for k, v := range resp.Header {
		w.Header()[k] = append([]string{}, v...)
	}
	for k := range resp.Trailer {
		w.Header().Add("Trailer", k)
	}
	w.WriteHeader(resp.StatusCode)
	parser := telemetry.NewSession(g.config.Backend, resp.Header.Get("Content-Type"))
	if path == "/api/v1/chat" {
		parser = telemetry.NewNativeSession(resp.Header.Get("Content-Type"))
	}
	parse := isChat && resp.StatusCode < 400 && resp.Header.Get("Content-Encoding") == ""
	buffer := make([]byte, 32*1024)
	controller := http.NewResponseController(w)
	for {
		n, readErr := resp.Body.Read(buffer)
		if n > 0 {
			if parse {
				parser.Observe(buffer[:n])
				if obs.TTFT.Value == nil && parser.HasOutput() {
					obs.TTFT = domain.Measured(float64(time.Since(start))/float64(time.Millisecond), domain.Milliseconds, "inspector", "streaming-ttft-v1")
				}
			}
			if _, writeErr := w.Write(buffer[:n]); writeErr != nil {
				obs.Outcome = "client_cancelled"
				obs.ErrorType = "client_cancellation"
				obs.ErrorOrigin = "client"
				break
			}
			_ = controller.Flush()
		}
		if readErr == io.EOF {
			for k, v := range resp.Trailer {
				w.Header()[http.TrailerPrefix+k] = append([]string{}, v...)
			}
			if parse {
				obs.Telemetry = parser.Complete()
			}
			break
		}
		if readErr != nil {
			obs.Outcome = "relay_failed"
			obs.ErrorType = "backend_crash"
			obs.ErrorOrigin = "backend"
			if r.Context().Err() != nil {
				obs.Outcome = "client_cancelled"
				obs.ErrorType = "client_cancellation"
				obs.ErrorOrigin = "client"
			}
			panic(http.ErrAbortHandler)
		}
	}
}
