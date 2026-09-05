package lifecycle

import (
	"context"
	_ "embed"
	"encoding/json"
	"path/filepath"
	"slices"
	"strings"
	"time"
	"unicode"
)

//go:embed config/runtime-compatibility.json
var compatibilityJSON []byte

func Matrix() []Compatibility {
	var matrix struct {
		SchemaVersion int
		Entries       []Compatibility
	}
	if json.Unmarshal(compatibilityJSON, &matrix) != nil || matrix.SchemaVersion != 1 {
		return nil
	}
	return matrix.Entries // decode per call: no mutable global slices escape
}

func classify(backend Backend, version string) Compatibility {
	for _, entry := range Matrix() {
		if entry.Backend == backend && versionToken(version, entry.VersionMatch) {
			entry.Limitations = append(entry.Limitations, "Evidence revision относится к reference implementation; Go Windows LIVE проверяется отдельно")
			return entry
		}
	}
	return Compatibility{Backend: backend, Status: "observation-only", Capabilities: []Capability{}, Limitations: []string{"Версия не включена в matrix; доступны только операции с пройденной безопасной capability probe"}}
}
func versionToken(text, token string) bool {
	for _, value := range strings.FieldsFunc(text, func(r rune) bool { return !unicode.IsLetter(r) && !unicode.IsDigit(r) && r != '.' }) {
		if value == token {
			return true
		}
	}
	return false
}

// Unknown versions are not promoted merely because --version worked. Read-only
// help probes establish each CLI operation; model API contracts remain gated.
func probeCapabilities(ctx context.Context, rt Runtime, target Target) Compatibility {
	compat := target.Compatibility
	if len(compat.Capabilities) != 0 {
		return compat
	}
	help := func(args ...string) string {
		result, err := rt.Execute(ctx, Command{Executable: target.Executable, Arguments: args, Timeout: 5 * time.Second})
		if err != nil || result.ExitCode != 0 {
			return ""
		}
		return result.Stdout + "\n" + result.Stderr
	}
	contains := func(text string, tokens ...string) bool {
		for _, token := range tokens {
			if !helpToken(text, token) {
				return false
			}
		}
		return text != ""
	}
	switch target.Backend {
	case Ollama:
		// Unknown Ollama API/env semantics are not established by CLI help alone.
		_ = help("serve", "--help")
	case LlamaCpp:
		h := help("--help")
		if contains(h, "host", "port", "model", "ctx-size", "n-gpu-layers", "threads", "parallel") {
			compat.Capabilities = []Capability{Start, Stop, Restart, Parameters}
		}
	case LMStudio:
		h := help("server", "start", "--help")
		stop := help("server", "stop", "--help")
		if contains(h, "port", "bind") && contains(stop, "stop") {
			compat.Capabilities = []Capability{Start, Stop, Restart}
			load := help("load", "--help")
			if contains(load, "gpu", "context-length", "ttl") {
				compat.Capabilities = append(compat.Capabilities, Parameters)
			}
		}
	}
	if len(compat.Capabilities) > 0 {
		compat.Status = "compatible"
		compat.Evidence = []string{"read-only operation-specific CLI help probes; not Windows LIVE"}
	}
	return compat
}

func helpToken(text, token string) bool {
	for _, value := range strings.FieldsFunc(text, func(r rune) bool { return !unicode.IsLetter(r) && !unicode.IsDigit(r) && r != '-' }) {
		if value == token || value == "--"+token {
			return true
		}
	}
	return false
}

func startPlan(target Target, values map[string]string, model string) (StartPlan, error) {
	if !validEndpoint(target.Endpoint) || endpoint(target.Backend, values) != target.Endpoint || !localFile(target.Executable, ".exe") {
		return StartPlan{}, ErrTarget
	}
	for id, value := range values {
		if normalized, err := Normalize(target.Backend, id, value); err != nil || normalized != value {
			return StartPlan{}, ErrParameter
		}
	}
	cmd := Command{Executable: target.Executable, Environment: map[string]string{}, Timeout: 30 * time.Second}
	plan := StartPlan{Command: cmd, Endpoint: target.Endpoint}
	add := func(id, flag string) {
		if values[id] != "" {
			plan.Command.Arguments = append(plan.Command.Arguments, flag, values[id])
		}
	}
	switch target.Backend {
	case Ollama:
		plan.Command.Arguments = []string{"serve"}
		plan.Command.Environment["OLLAMA_HOST"] = "127.0.0.1:" + values["local-port"]
		for id, key := range map[string]string{"context": "OLLAMA_CONTEXT_LENGTH", "parallel": "OLLAMA_NUM_PARALLEL", "max-loaded": "OLLAMA_MAX_LOADED_MODELS", "max-queue": "OLLAMA_MAX_QUEUE"} {
			if values[id] != "" {
				plan.Command.Environment[key] = values[id]
			}
		}
		if values["keep-alive"] != "" {
			plan.Command.Environment["OLLAMA_KEEP_ALIVE"] = values["keep-alive"] + "s"
		}
		plan.AllowedImages = []string{"ollama.exe"}
	case LlamaCpp:
		if !localFile(model, ".gguf") {
			return StartPlan{}, ErrModel
		}
		plan.Command.Arguments = []string{"--host", "127.0.0.1", "--port", values["local-port"], "--model", model}
		add("context", "--ctx-size")
		add("cpu-threads", "--threads")
		add("parallel", "--parallel")
		if gpu := values["gpu-layers"]; gpu != "" {
			if gpu == "off" {
				gpu = "0"
			}
			plan.Command.Arguments = append(plan.Command.Arguments, "--n-gpu-layers", gpu)
		}
		plan.AllowedImages = []string{"llama-server.exe"}
	case LMStudio:
		plan.Command.Arguments = []string{"server", "start", "--port", values["local-port"], "--bind", "127.0.0.1"}
		plan.Detached = true
		plan.AllowedImages = []string{"LM Studio.exe", "lms.exe", "llmster.exe"}
	default:
		return StartPlan{}, ErrUnsupported
	}
	return plan, nil
}

func officialStop(target Target) *Command {
	if target.Backend != LMStudio {
		return nil
	}
	return &Command{Executable: target.Executable, Arguments: []string{"server", "stop"}, Timeout: 15 * time.Second}
}

func ready(ctx context.Context, rt Runtime, target Target) bool {
	ctx, cancel := context.WithTimeout(ctx, 3*time.Second)
	defer cancel()
	path := "v1/models"
	if target.Backend == Ollama {
		path = "api/tags"
	}
	if target.Backend == LlamaCpp {
		path = "health"
	}
	body, err := rt.HTTP(ctx, "GET", target.Endpoint+path, nil)
	if err != nil {
		return false
	}
	if target.Backend == LlamaCpp {
		var value struct{ Status string }
		return json.Unmarshal(body, &value) == nil && value.Status == "ok"
	}
	_, err = modelIDs(body, target.Backend, false)
	return err == nil
}

func awaitReady(ctx context.Context, rt Runtime, target Target) bool {
	ctx, cancel := context.WithTimeout(ctx, 5*time.Minute)
	defer cancel()
	for {
		if ready(ctx, rt, target) {
			return true
		}
		timer := time.NewTimer(200 * time.Millisecond)
		select {
		case <-ctx.Done():
			timer.Stop()
			return false
		case <-timer.C:
		}
	}
}

func listModels(ctx context.Context, rt Runtime, target Target, loaded bool) ([]string, error) {
	var body []byte
	var err error
	switch target.Backend {
	case Ollama:
		path := "api/tags"
		if loaded {
			path = "api/ps"
		}
		body, err = rt.HTTP(ctx, "GET", target.Endpoint+path, nil)
	case LMStudio:
		verb := "ls"
		if loaded {
			verb = "ps"
		}
		var result CommandResult
		result, err = rt.Execute(ctx, Command{Executable: target.Executable, Arguments: []string{verb, "--json"}, Timeout: 10 * time.Second})
		if err == nil && result.ExitCode != 0 {
			err = ErrCommand
		}
		body = []byte(result.Stdout)
	case LlamaCpp:
		if !loaded {
			return []string{}, nil
		}
		body, err = rt.HTTP(ctx, "GET", target.Endpoint+"v1/models", nil)
	default:
		return nil, ErrUnsupported
	}
	if err != nil {
		return nil, err
	}
	return modelIDs(body, target.Backend, target.Backend == LMStudio)
}

// Only official top-level identity fields are accepted. A substring or an
// unrelated nested JSON string is not evidence that a selected model is loaded.
func modelIDs(body []byte, backend Backend, cli bool) ([]string, error) {
	if len(body) > 65536 {
		return nil, ErrModel
	}
	type record struct {
		ID         string `json:"id"`
		Name       string `json:"name"`
		Model      string `json:"model"`
		Key        string `json:"modelKey"`
		Path       string `json:"path"`
		Identifier string `json:"identifier"`
	}
	var records []record
	if cli {
		if json.Unmarshal(body, &records) != nil || records == nil {
			return nil, ErrModel
		}
	} else {
		var envelope struct {
			Models *[]record `json:"models"`
			Data   *[]record `json:"data"`
		}
		if json.Unmarshal(body, &envelope) != nil {
			return nil, ErrModel
		}
		if backend == Ollama {
			if envelope.Models == nil {
				return nil, ErrModel
			}
			records = *envelope.Models
		} else {
			if envelope.Data == nil {
				return nil, ErrModel
			}
			records = *envelope.Data
		}
	}
	if len(records) > 1024 {
		return nil, ErrModel
	}
	ids := []string{}
	for _, record := range records {
		var candidates []string
		if cli {
			candidates = []string{record.Key, record.Path, record.Identifier}
		} else if backend == Ollama {
			candidates = []string{record.Name, record.Model}
		} else {
			candidates = []string{record.ID}
		}
		for _, id := range candidates {
			if validModel(id) && !slices.Contains(ids, id) {
				ids = append(ids, id)
			}
		}
	}
	slices.Sort(ids)
	return ids, nil
}

func loadModel(ctx context.Context, rt Runtime, target Target, values map[string]string, model string) error {
	if !validModel(model) {
		return ErrModel
	}
	switch target.Backend {
	case Ollama:
		keep := "5m"
		if values["keep-alive"] != "" {
			keep = values["keep-alive"] + "s"
		}
		body, _ := json.Marshal(struct {
			Model     string `json:"model"`
			Prompt    string `json:"prompt"`
			Stream    bool   `json:"stream"`
			KeepAlive string `json:"keep_alive"`
		}{model, "", false, keep})
		if _, err := rt.HTTP(ctx, "POST", target.Endpoint+"api/generate", body); err != nil {
			return err
		}
	case LMStudio:
		args := []string{"load", model}
		for _, pair := range [][2]string{{"gpu-offload", "--gpu"}, {"context", "--context-length"}, {"model-ttl", "--ttl"}} {
			if value := values[pair[0]]; value != "" && !(pair[0] == "gpu-offload" && value == "auto") {
				args = append(args, pair[1], value)
			}
		}
		result, err := rt.Execute(ctx, Command{Executable: target.Executable, Arguments: args, Timeout: 5 * time.Minute})
		if err != nil || result.ExitCode != 0 {
			return ErrCommand
		}
	default:
		return ErrUnsupported
	}
	return confirmModel(ctx, rt, target, model)
}
func confirmModel(ctx context.Context, rt Runtime, target Target, model string) error {
	ids, err := listModels(ctx, rt, target, true)
	if err != nil {
		return ErrModel
	}
	if slices.Contains(ids, model) {
		return nil
	}
	if target.Backend == LlamaCpp && slices.Contains(ids, strings.TrimSuffix(filepath.Base(model), filepath.Ext(model))) {
		return nil
	}
	return ErrModel
}
