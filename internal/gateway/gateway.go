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
	"github.com/Just9120/llm-inspector/internal/state"
	"github.com/Just9120/llm-inspector/internal/telemetry"
)

type Gateway struct {
	config      Config
	target      *url.URL
	transport   *http.Transport
	sink        chan<- domain.Observation
	mu          sync.Mutex
	server      *http.Server
	listener    net.Listener
	dropped     atomic.Uint64
	active      atomic.Int64
	live        *state.Live
	correlation *state.Correlation
	operations  *state.Operations
	monitor     atomic.Pointer[monitorHolder]
	authorizer  atomic.Pointer[authorizerHolder]
	facts       atomic.Pointer[domain.RuntimeFacts]
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
	return &Gateway{config: c, target: target, transport: tr, sink: sink, live: state.NewLive(nil), correlation: state.NewCorrelation(), operations: state.NewOperations()}, nil
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

func (g *Gateway) Dropped() uint64                   { return g.dropped.Load() }
func (g *Gateway) LiveSnapshot() domain.LiveSnapshot { return g.live.Snapshot() }
func (g *Gateway) ActiveCount() int64                { return g.active.Load() }

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
	authorizedRemote, denialStatus, denialCode := g.authorizeIngress(r)
	if denialStatus != 0 {
		if denialStatus == 401 {
			w.Header().Set("WWW-Authenticate", "Bearer")
		}
		safeResponse(w, denialStatus, denialCode)
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
	var resources domain.ResourceSession
	if isChat {
		operationID := ""
		if obs.Correlation != nil {
			operationID = obs.Correlation.OperationID
		}
		resources = g.startResources(domain.RequestResourceContext{RequestID: obs.RequestID, OperationID: operationID, BackendURL: g.config.BackendURL})
		if resources != nil {
			resourceCall(func() { resources.StageChanged(state.ProtocolStage(domain.PromptProcessing)) })
		}
		g.active.Add(1)
		g.live.Start(obs.RequestID, client, start)
		g.live.Stage(obs.RequestID, state.ProtocolStage(domain.PromptProcessing))
		defer func() {
			g.active.Add(-1)
			obs.ErrorOrigin = errorOrigin(obs.ErrorType)
			g.live.Finish(obs.RequestID, obs.Outcome, obs.ErrorType)
			obs.ContextChange = g.correlation.Observe(obs.Correlation, obs.Client, obs.Telemetry.Backend, obs.Telemetry.ContextUsage)
			obs.DurationMS = float64(time.Since(start)) / float64(time.Millisecond)
			if base := g.facts.Load(); base != nil {
				facts := *base
				facts.ModelVersion = domain.TechnicalIdentifier(obs.Telemetry.Model)
				if evidence, ok := resources.(domain.ResourceRuntimeEvidence); ok && !g.config.Remote {
					resourceCall(func() { facts.GPUDriverVersion = domain.TechnicalIdentifier(evidence.GPUDriverVersion()) })
				}
				obs.Runtime = &facts
			}
			obs.Operation = g.operations.Observe(obs)
			select {
			case g.sink <- obs:
			default:
				g.dropped.Add(1)
			}
			if resources != nil {
				stage := domain.Completed
				if obs.Outcome == "client_cancelled" {
					stage = domain.Cancelled
				} else if obs.Outcome != "completed" || obs.ErrorType != "none" {
					stage = domain.Failed
				}
				resourceCall(func() { resources.StageChanged(state.ProtocolStage(stage)) })
				resourceCall(resources.Complete)
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
	if resources != nil && out.Body != nil {
		out.Body = &resourceBody{ReadCloser: out.Body, session: resources}
	}
	if path == "/v1/chat/completions" && r.Header.Get("Content-Encoding") == "" && r.Body != nil {
		capture := &requestCapture{inner: out.Body, session: telemetry.NewRequestSession(), expected: r.ContentLength}
		out.Body = capture
		defer func() { obs.Agent.AvailableTools, obs.Agent.ToolResults = capture.Result() }()
	}
	scrubHopHeaders(out.Header)
	if authorizedRemote {
		out.Header.Del("Authorization")
	}
	for _, k := range append(append([]string{}, identityHeaders...), correlationHeaders...) {
		out.Header.Del(k)
	}
	resp, err := g.transport.RoundTrip(out)
	if err != nil {
		obs.Outcome = "backend_unavailable"
		obs.ErrorType = transportError(err, false)
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
		status := 502
		if obs.ErrorType == "timeout" {
			status = 504
		}
		if obs.Outcome != "client_cancelled" {
			obs.HTTPStatus = &status
		}
		safeResponse(w, status, "backend_unavailable")
		return
	}
	defer resp.Body.Close()
	obs.HTTPStatus = &resp.StatusCode
	if resp.StatusCode >= 400 {
		obs.ErrorType = httpError(resp.StatusCode, false)
		obs.ErrorOrigin = "backend"
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
	errorParser := telemetry.NewErrorSession()
	parseError := isChat && resp.StatusCode >= 400 && resp.Header.Get("Content-Encoding") == ""
	buffer := make([]byte, 32*1024)
	controller := http.NewResponseController(w)
	for {
		n, readErr := resp.Body.Read(buffer)
		if n > 0 {
			if isChat && resp.StatusCode < 400 {
				g.live.Stage(obs.RequestID, state.ProtocolStage(domain.Generating))
				if resources != nil {
					resourceCall(func() { resources.StageChanged(state.ProtocolStage(domain.Generating)) })
				}
			}
			if parseError {
				errorParser.Observe(buffer[:n])
			}
			if parse {
				parser.Observe(buffer[:n])
				if stage := parser.Stage(); stage != nil {
					g.live.Stage(obs.RequestID, *stage)
					if resources != nil {
						resourceCall(func() { resources.StageChanged(*stage) })
					}
				}
				if obs.TTFT.Value == nil && parser.HasOutput() {
					obs.TTFT = domain.Derived(float64(time.Since(start))/float64(time.Millisecond), domain.Milliseconds, domain.Calculated, "streaming-ttft-v1", "first-output-monotonic-v1")
				}
			}
			written, writeErr := w.Write(buffer[:n])
			if resources != nil && written > 0 {
				resourceCall(func() { resources.AddReceived(written) })
			}
			if writeErr != nil {
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
				obs.Agent = parser.AgentResponse()
			}
			if parseError {
				obs.ErrorType = httpError(resp.StatusCode, errorParser.ContextOverflow())
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
