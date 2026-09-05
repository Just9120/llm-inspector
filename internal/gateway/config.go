// Package gateway owns the loopback HTTP relay; it never owns persistent data.
package gateway

import (
	"errors"
	"net"
	"net/url"
	"strconv"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Config struct {
	Backend    domain.Backend
	BackendURL string
	Port       int
	Remote     bool
}

func DefaultConfig(backend domain.Backend) Config {
	port := map[domain.Backend]string{domain.Ollama: "11434", domain.LlamaCpp: "8080", domain.LMStudio: "1234"}[backend]
	return Config{Backend: backend, BackendURL: "http://127.0.0.1:" + port + "/", Port: 5117}
}

func (c Config) target(allowTestPort bool) (*url.URL, error) {
	invalid := errors.New("некорректная конфигурация подключения")
	if c.Port < 0 || c.Port > 65535 || (c.Port == 0 && !allowTestPort) {
		return nil, invalid
	}
	if c.Backend != domain.Ollama && c.Backend != domain.LlamaCpp && c.Backend != domain.LMStudio {
		return nil, invalid
	}
	u, err := url.Parse(c.BackendURL)
	if err != nil || u.Opaque != "" || u.User != nil || u.RawQuery != "" || u.ForceQuery || u.Fragment != "" || strings.Contains(c.BackendURL, "#") || (u.Path != "" && u.Path != "/") || u.RawPath != "" || u.Hostname() == "" {
		return nil, invalid
	}
	if u.Scheme != "http" && u.Scheme != "https" {
		return nil, invalid
	}
	port := u.Port()
	if port != "" {
		n, e := strconv.Atoi(port)
		if e != nil || n < 1 || n > 65535 {
			return nil, invalid
		}
	}
	host := u.Hostname()
	if c.Remote {
		if u.Scheme != "https" || !tailscaleHost(host) {
			return nil, invalid
		}
	} else {
		if strings.EqualFold(host, "localhost") {
			host = "127.0.0.1"
		}
		ip := net.ParseIP(host)
		if ip == nil || !ip.IsLoopback() {
			return nil, invalid
		}
		if port != "" {
			u.Host = net.JoinHostPort(host, port)
		} else if strings.Contains(host, ":") {
			u.Host = "[" + host + "]"
		} else {
			u.Host = host
		}
	}
	u.Path = "/"
	return u, nil
}

func (c Config) Validate() error { _, err := c.target(false); return err }

func tailscaleHost(host string) bool {
	host = strings.ToLower(host)
	if !strings.HasSuffix(host, ".ts.net") || len(host) > 253 {
		return false
	}
	labels := strings.Split(host, ".")
	if len(labels) < 4 {
		return false
	}
	for _, label := range labels {
		if len(label) == 0 || len(label) > 63 || label[0] == '-' || label[len(label)-1] == '-' {
			return false
		}
		for _, c := range label {
			if !(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-' {
				return false
			}
		}
	}
	return true
}

// ParseLaunch accepts a closed set of options, without echoing invalid values.
func ParseLaunch(args []string) (Config, bool, error) {
	c := DefaultConfig(domain.Ollama)
	background := false
	values := map[string]string{}
	invalid := errors.New("неизвестный или некорректный параметр запуска")
	for _, arg := range args {
		if strings.EqualFold(arg, "--background") {
			if background {
				return c, false, invalid
			}
			background = true
			continue
		}
		key, val, ok := strings.Cut(arg, "=")
		key = strings.ToLower(key)
		if !ok || val == "" {
			return c, false, invalid
		}
		switch key {
		case "--backend", "--backend-url", "--remote-backend-url", "--listener-port":
		default:
			return c, false, invalid
		}
		if _, dup := values[key]; dup {
			return c, false, invalid
		}
		values[key] = val
	}
	if v, ok := values["--backend"]; ok {
		v = strings.ToLower(v)
		switch v {
		case "llama.cpp", "llamacpp":
			v = string(domain.LlamaCpp)
		case "lmstudio":
			v = string(domain.LMStudio)
		}
		c = DefaultConfig(domain.Backend(v))
	}
	if v, ok := values["--backend-url"]; ok {
		c.BackendURL = v
	}
	if v, ok := values["--remote-backend-url"]; ok {
		if _, dup := values["--backend-url"]; dup {
			return c, false, invalid
		}
		c.BackendURL = v
		c.Remote = true
	}
	if v, ok := values["--listener-port"]; ok {
		n, e := strconv.Atoi(v)
		if e != nil {
			return c, false, invalid
		}
		c.Port = n
	}
	return c, background, c.Validate()
}
