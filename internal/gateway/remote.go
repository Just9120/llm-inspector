package gateway

import (
	"errors"
	"net"
	"net/http"
	"strings"
)

type RemoteAuthorizer interface {
	Enabled() bool
	IsBearerTokenValid(string) bool
}
type authorizerHolder struct{ authorizer RemoteAuthorizer }

func (g *Gateway) SetRemoteAuthorizer(a RemoteAuthorizer) error {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.server != nil {
		return errors.New("подключение authorizer требует остановленного proxy")
	}
	if a == nil {
		g.authorizer.Store(nil)
	} else {
		g.authorizer.Store(&authorizerHolder{a})
	}
	return nil
}
func loopbackPeer(r *http.Request) bool {
	host, _, err := net.SplitHostPort(r.RemoteAddr)
	if err != nil {
		return false
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

// Serve identity is accepted only from the loopback listener. Header presence
// alone never authorizes ingress; enabled application credentials are required.
// Tailscale deployment/ACL/Funnel remain explicit user-run preconditions.
func (g *Gateway) authorizeIngress(r *http.Request) (remote bool, status int, code string) {
	if !loopbackPeer(r) {
		return false, 403, "ingress_requires_loopback_peer"
	}
	if localIngress(r) {
		return false, 0, ""
	}
	status, code = 403, "remote_access_disabled"
	defer func() {
		if recover() != nil {
			remote = false
			status = 403
			code = "remote_access_disabled"
		}
	}()
	host := r.Host
	if parsed, _, err := net.SplitHostPort(host); err == nil {
		host = parsed
	}
	login := r.Header.Values("Tailscale-User-Login")
	if !tailscaleHost(host) || len(login) != 1 || len(login[0]) > 1024 || strings.TrimSpace(login[0]) == "" {
		return false, 403, "remote_ingress_not_private_serve"
	}
	for _, c := range login[0] {
		if c < ' ' || c == 127 {
			return false, 403, "remote_ingress_not_private_serve"
		}
	}
	holder := g.authorizer.Load()
	if holder == nil || !holder.authorizer.Enabled() {
		return false, 403, "remote_access_disabled"
	}
	authorization := r.Header.Values("Authorization")
	if len(authorization) != 1 || len(authorization[0]) != 50 || !strings.EqualFold(authorization[0][:7], "Bearer ") || !holder.authorizer.IsBearerTokenValid(authorization[0][7:]) {
		return false, 401, "remote_authentication_failed"
	}
	return true, 0, ""
}
