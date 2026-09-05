package lifecycle

import (
	"context"
	"crypto/sha256"
	"fmt"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
)

func TestParameterAllowlistAndBoundaries(t *testing.T) {
	for _, backend := range []Backend{Ollama, LlamaCpp, LMStudio} {
		profile, err := Profile(backend)
		if err != nil {
			t.Fatal(err)
		}
		for _, p := range profile {
			if _, err = Normalize(backend, p.ID, ""); err != nil {
				t.Fatal(p, err)
			}
			if p.Maximum > 0 {
				for _, value := range []string{"-1", "NaN", "Infinity", "1e4", "--host", "1\n2", "999999999999999999999999"} {
					if _, err = Normalize(backend, p.ID, value); err == nil {
						t.Fatal(backend, p.ID, value)
					}
				}
			}
		}
		for _, id := range []string{"host", "bind", "cors", "environment", "arguments", "download", "service"} {
			if _, err = Normalize(backend, id, "anything"); err == nil {
				t.Fatal("arbitrary control", id)
			}
		}
	}
	valid := []struct {
		b               Backend
		id, value, want string
	}{{Ollama, "keep-alive", "0", "0"}, {Ollama, "context", " 8192 ", "8192"}, {LlamaCpp, "gpu-layers", "all", "all"}, {LlamaCpp, "gpu-layers", "999", "999"}, {LMStudio, "gpu-offload", "0.50", "0.5"}, {LMStudio, "gpu-offload", "auto", "auto"}}
	for _, v := range valid {
		got, err := Normalize(v.b, v.id, v.value)
		if err != nil || got != v.want {
			t.Fatal(v, got, err)
		}
	}
	for _, value := range []string{"-0.5", "1.1", "NaN", "Inf", "1e-1", "0,5", "--remote"} {
		if _, err := Normalize(LMStudio, "gpu-offload", value); err == nil {
			t.Fatal(value)
		}
	}
	for _, value := range []string{"--help", "bad\x00", "bad\n"} {
		if _, err := Normalize(LMStudio, "model-id", value); err == nil {
			t.Fatal("model injection")
		}
	}
}

func TestTypedLaunchAndLoadPlans(t *testing.T) {
	for _, backend := range []Backend{Ollama, LlamaCpp, LMStudio} {
		m, rt, _ := fakeFor(t, backend)
		confirm(t, m)
		s := m.Snapshot()
		values := Defaults(backend)
		values["context"] = "8192"
		if backend == Ollama {
			values["keep-alive"] = "45"
			values["parallel"] = "4"
		}
		if backend == LlamaCpp {
			values["gpu-layers"] = "off"
		}
		p, err := startPlan(*s.Target, values, filepath.Join(t.TempDir(), "a b.gguf"))
		if err != nil {
			t.Fatal(err)
		}
		joined := strings.Join(p.Command.Arguments, " ")
		if strings.Contains(joined, "0.0.0.0") || strings.Contains(joined, "--cors") {
			t.Fatal(p)
		}
		if backend == Ollama && (p.Command.Environment["OLLAMA_HOST"] != "127.0.0.1:11434" || p.Command.Environment["OLLAMA_KEEP_ALIVE"] != "45s") {
			t.Fatal(p)
		}
		if backend == LlamaCpp && (!strings.Contains(joined, "--n-gpu-layers 0") || !strings.Contains(joined, "--ctx-size 8192")) {
			t.Fatal(p)
		}
		if backend == LMStudio {
			if !p.Detached || !strings.Contains(joined, "--bind 127.0.0.1") {
				t.Fatal(p)
			}
			values["gpu-offload"] = "auto"
			if err := loadModel(context.Background(), rt, *s.Target, values, "model:exact"); err != nil {
				t.Fatal(err)
			}
			for _, c := range rt.commands {
				if len(c.Arguments) > 0 && c.Arguments[0] == "load" && strings.Contains(strings.Join(c.Arguments, " "), "--gpu") {
					t.Fatal("auto must omit GPU flag")
				}
			}
		}
		values["host"] = "0.0.0.0"
		if _, err = startPlan(*s.Target, values, ""); err == nil {
			t.Fatal("unknown field")
		}
	}
}

func TestModelJSONStrictIdentityAndBounds(t *testing.T) {
	for _, body := range []string{`null`, `{}`, `{"models":null}`, `{"models":"x"}`, `{"models":[{"name":123}]}`, `{"models":[]} {}`, strings.Repeat("x", 65537)} {
		if _, err := modelIDs([]byte(body), Ollama, false); err == nil {
			t.Fatal("invalid schema accepted", body[:min(50, len(body))])
		}
	}
	ids, err := modelIDs([]byte(`{"models":[{"name":"a:latest","other":{"name":"secret"}},{"model":"a:latest"},{"name":"b"}]}`), Ollama, false)
	if err != nil || !reflect.DeepEqual(ids, []string{"a:latest", "b"}) {
		t.Fatal(ids, err)
	}
	ids, err = modelIDs([]byte(`[{"modelKey":"owner/name","identifier":"alias","path":"owner/file.gguf","other":"not-id"}]`), LMStudio, true)
	if err != nil || len(ids) != 3 {
		t.Fatal(ids, err)
	}
	if _, err = modelIDs([]byte(`{"models":[]}`), LMStudio, true); err == nil {
		t.Fatal("wrong CLI envelope")
	}
}

func TestEmbeddedCompatibilityPreservesReferenceMatrix(t *testing.T) {
	// Frozen LF-normalized legacy matrix, source ee32a97. Historical verified
	// entries do not become Go LIVE evidence solely because this hash matches.
	if fmt.Sprintf("%x", sha256.Sum256([]byte(strings.ReplaceAll(string(compatibilityJSON), "\r\n", "\n")))) != "8b405cc1d4e445c9b4bc2bf2012e767f2c785945178ac32b68eac19579d07244" {
		t.Fatal("matrix drift")
	}
	a := Matrix()
	a[0].Capabilities[0] = "bad"
	if Matrix()[0].Capabilities[0] != Start {
		t.Fatal("mutable matrix")
	}
	for _, bad := range []string{"0.33.20", "0.33.2.1", "x0.33.2", "0.33.2x"} {
		if versionToken(bad, "0.33.2") {
			t.Fatal(bad)
		}
	}
}

type helpRuntime struct {
	*fakeRuntime
	help map[string]string
}

func (r helpRuntime) Execute(ctx context.Context, c Command) (CommandResult, error) {
	if value, ok := r.help[strings.Join(c.Arguments, " ")]; ok {
		return CommandResult{Stdout: value}, nil
	}
	return r.fakeRuntime.Execute(ctx, c)
}
func TestUnknownCapabilityProbesAreOperationSpecific(t *testing.T) {
	rt := helpRuntime{fakeRuntime: &fakeRuntime{}, help: map[string]string{"server start --help": "--port --bind", "server stop --help": "server stop"}}
	target := Target{Backend: LMStudio, Executable: filepath.Join(t.TempDir(), "lms.exe"), Compatibility: classify(LMStudio, "99.0")}
	result := probeCapabilities(context.Background(), rt, target)
	if !reflect.DeepEqual(result.Capabilities, []Capability{Start, Stop, Restart}) || result.Status != "compatible" {
		t.Fatal(result)
	}
	rt.help["load --help"] = "--gpu --context-length --ttl"
	result = probeCapabilities(context.Background(), rt, target)
	if !reflect.DeepEqual(result.Capabilities, []Capability{Start, Stop, Restart, Parameters}) {
		t.Fatal(result)
	}
	rt.help["server start --help"] = "--port-unsafe --bind-public"
	if result = probeCapabilities(context.Background(), rt, target); len(result.Capabilities) != 0 {
		t.Fatal("substring capability", result)
	}
	target.Backend = LlamaCpp
	target.Compatibility = classify(LlamaCpp, "99")
	rt.help["--help"] = "--host --port --model --ctx-size --n-gpu-layers --threads --parallel"
	if result = probeCapabilities(context.Background(), rt, target); result.Status != "compatible" {
		t.Fatal(result)
	}
}
