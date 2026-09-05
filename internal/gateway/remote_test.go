package gateway

import (
	"context"
	"encoding/json"
	"github.com/Just9120/llm-inspector/internal/domain"
	"github.com/Just9120/llm-inspector/internal/remote"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

type remoteFixtureStore struct{ value remote.Stored }

func (s *remoteFixtureStore) Load(context.Context) (remote.Stored, error) {
	v := s.value
	v.Token = append([]byte(nil), v.Token...)
	return v, nil
}
func (s *remoteFixtureStore) Save(_ context.Context, v remote.Stored) error {
	s.value = v
	s.value.Token = append([]byte(nil), v.Token...)
	return nil
}
func enabledRemote(t *testing.T) (*remote.Manager, string) {
	t.Helper()
	m := remote.NewManager(&remoteFixtureStore{})
	if err := m.Initialize(t.Context()); err != nil {
		t.Fatal(err)
	}
	v, err := m.Enable(t.Context(), true)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(m.Close)
	return m, *v.OneTimeToken
}
func privateServeRequest(token string) *http.Request {
	r := httptest.NewRequest(http.MethodGet, "http://node.tailnet.ts.net/v1/models", nil)
	r.RemoteAddr = "127.0.0.1:1234"
	r.Header.Set("Tailscale-User-Login", "fixture@example.invalid")
	r.Header.Set("Authorization", "Bearer "+token)
	return r
}
func TestPrivateServeAuthMatrixAndLoopbackPeerBoundary(t *testing.T) {
	m, token := enabledRemote(t)
	g, _ := New(DefaultConfig(domain.Ollama), nil)
	g.SetRemoteAuthorizer(m)
	if remote, status, _ := g.authorizeIngress(privateServeRequest(token)); !remote || status != 0 {
		t.Fatal("authorized private Serve denied")
	}
	tests := []struct {
		name   string
		mutate func(*http.Request)
		status int
	}{
		{"funnel_without_identity", func(r *http.Request) { r.Header.Del("Tailscale-User-Login") }, 403},
		{"public_host", func(r *http.Request) { r.Host = "public.example" }, 403},
		{"fake_suffix", func(r *http.Request) { r.Host = "node.tailnet.ts.net.evil.example" }, 403},
		{"duplicate_identity", func(r *http.Request) { r.Header.Add("Tailscale-User-Login", "second") }, 403},
		{"empty_identity", func(r *http.Request) { r.Header.Set("Tailscale-User-Login", " ") }, 403},
		{"oversized_identity", func(r *http.Request) { r.Header.Set("Tailscale-User-Login", strings.Repeat("a", 1025)) }, 403},
		{"missing_bearer", func(r *http.Request) { r.Header.Del("Authorization") }, 401},
		{"wrong_bearer", func(r *http.Request) { r.Header.Set("Authorization", "Bearer "+strings.Repeat("a", 43)) }, 401},
		{"duplicate_bearer", func(r *http.Request) { r.Header.Add("Authorization", "Bearer "+token) }, 401},
		{"basic_auth", func(r *http.Request) { r.Header.Set("Authorization", "Basic "+token) }, 401},
		{"network_peer", func(r *http.Request) { r.RemoteAddr = "192.0.2.1:1234" }, 403},
		{"local_host_forwarded_identity", func(r *http.Request) { r.Host = "127.0.0.1" }, 403},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			r := privateServeRequest(token)
			test.mutate(r)
			w := httptest.NewRecorder()
			g.ServeHTTP(w, r)
			if w.Code != test.status {
				t.Fatalf("status %d", w.Code)
			}
			if test.status == 401 && w.Header().Get("WWW-Authenticate") != "Bearer" {
				t.Fatal("missing challenge")
			}
			if strings.Contains(w.Body.String(), token) || strings.Contains(w.Body.String(), "fixture@example.invalid") {
				t.Fatal("secret/identity in denied response")
			}
		})
	}
	local := httptest.NewRequest(http.MethodGet, "http://localhost/v1/models", nil)
	local.RemoteAddr = "[::1]:1234"
	local.Header.Set("Authorization", "Bearer backend-only")
	if remote, status, _ := g.authorizeIngress(local); remote || status != 0 {
		t.Fatal("ordinary local backend credentials denied")
	}
	m.Disable(t.Context())
	if _, status, _ := g.authorizeIngress(privateServeRequest(token)); status != 403 {
		t.Fatal("disabled remote authorized")
	}
}
func TestPrivateServeRelayScrubsSecretsAndPreservesBytes(t *testing.T) {
	m, token := enabledRemote(t)
	const response = "data: {\"choices\":[{\"delta\":{\"content\":\"PRIVATE_RESPONSE\"}}]}\n\ndata: [DONE]\n\n"
	const request = `{"messages":[{"role":"user","content":"PRIVATE_PROMPT"}]}`
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Header.Get("Authorization") != "" {
			t.Error("application token reached backend")
		}
		for _, key := range identityHeaders {
			if r.Header.Get(key) != "" {
				t.Error("identity reached backend")
			}
		}
		body, _ := io.ReadAll(r.Body)
		if string(body) != request {
			t.Error("request changed")
		}
		w.Header().Set("Content-Type", "text/event-stream")
		io.WriteString(w, response)
	}))
	defer backend.Close()
	observations := make(chan domain.Observation, 1)
	g, err := newGateway(Config{Backend: domain.Ollama, BackendURL: backend.URL, Port: 0}, observations, true)
	if err != nil {
		t.Fatal(err)
	}
	g.SetRemoteAuthorizer(m)
	address, err := g.Start()
	if err != nil {
		t.Fatal(err)
	}
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), time.Second)
		defer cancel()
		g.Stop(ctx)
	}()
	if g.SetRemoteAuthorizer(nil) == nil {
		t.Fatal("live authorizer replacement")
	}
	r, _ := http.NewRequest(http.MethodPost, address+"/clients/hermes/v1/chat/completions", strings.NewReader(request))
	r.Host = "node.tailnet.ts.net"
	r.Header.Set("Authorization", "Bearer "+token)
	for _, key := range identityHeaders {
		r.Header.Set(key, "SYNTHETIC_IDENTITY")
	}
	resp, err := http.DefaultClient.Do(r)
	if err != nil {
		t.Fatal(err)
	}
	body, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if resp.StatusCode != 200 || string(body) != response {
		t.Fatal("remote bytes changed")
	}
	select {
	case o := <-observations:
		data, _ := json.Marshal(o)
		for _, secret := range []string{token, "SYNTHETIC_IDENTITY", "PRIVATE_PROMPT", "PRIVATE_RESPONSE"} {
			if strings.Contains(string(data), secret) {
				t.Fatal("secret escaped telemetry")
			}
		}
		if o.Client != domain.Hermes {
			t.Fatal("reserved client attribution")
		}
	case <-time.After(time.Second):
		t.Fatal("missing observation")
	}
	previous := token
	rotation, err := m.Rotate(t.Context(), true)
	if err != nil {
		t.Fatal(err)
	}
	if _, status, _ := g.authorizeIngress(privateServeRequest(previous)); status != 401 {
		t.Fatal("old token still valid")
	}
	if _, status, _ := g.authorizeIngress(privateServeRequest(*rotation.OneTimeToken)); status != 0 {
		t.Fatal("new token denied")
	}
}

type panickingAuthorizer struct{}

func (panickingAuthorizer) Enabled() bool                  { panic("authorizer failure") }
func (panickingAuthorizer) IsBearerTokenValid(string) bool { return true }
func TestMissingOrFailedRemoteAuthorizerFailsClosed(t *testing.T) {
	g, _ := New(DefaultConfig(domain.Ollama), nil)
	for _, authorizer := range []RemoteAuthorizer{nil, panickingAuthorizer{}} {
		g.SetRemoteAuthorizer(authorizer)
		if remote, status, _ := g.authorizeIngress(privateServeRequest(strings.Repeat("a", 43))); remote || status != 403 {
			t.Fatal("authorizer unavailable but ingress open")
		}
	}
}
