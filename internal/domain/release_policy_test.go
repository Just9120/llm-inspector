package domain

import (
	"encoding/json"
	"go/parser"
	"go/token"
	"io/fs"
	"os"
	"path/filepath"
	"regexp"
	"slices"
	"strconv"
	"strings"
	"testing"
)

func repositoryText(t *testing.T, path string) string {
	t.Helper()
	b, err := os.ReadFile(filepath.Join("../..", path))
	if err != nil {
		t.Fatal(err)
	}
	return strings.ReplaceAll(string(b), "\r\n", "\n")
}

func TestReleasePreservesTrustedFinalTagAndSplitPermissions(t *testing.T) {
	w := repositoryText(t, ".github/workflows/release.yml")
	build, publish, found := strings.Cut(w, "\n  publish:\n")
	if !found {
		t.Fatal("publish job missing")
	}
	for _, required := range []string{
		"\n    tags:\n      - \"v*\"\n", "\npermissions:\n  contents: read\n", "cancel-in-progress: false",
		"'^v(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)$'",
		"$remoteRef = 'refs/remotes/origin/main'", "git fetch --no-tags origin \"main:${remoteRef}\"",
		"git merge-base --is-ancestor $env:SOURCE_SHA $remoteRef", "./scripts/build-windows.ps1",
		"subject-checksums: release-payload/assets/SHA256SUMS.txt", "--verify-tag",
		"\n      contents: write\n      id-token: write\n      attestations: write\n",
		"GH_REPO: ${{ github.repository }}", "persist-credentials: false",
	} {
		if !strings.Contains(w, required) {
			t.Errorf("missing release gate %q", required)
		}
	}
	for _, forbidden := range []string{"pull_request", "workflow_dispatch", "workflow_run", "secrets:", "environment:", "continue-on-error:", "--prerelease", "release/v1.0", "dotnet "} {
		if strings.Contains(w, forbidden) {
			t.Errorf("unsafe/obsolete release field %q", forbidden)
		}
	}
	for _, capability := range []string{"contents: write", "id-token: write", "attestations: write", "GH_TOKEN:"} {
		if strings.Contains(build, capability) {
			t.Errorf("trusted capability in build %s", capability)
		}
	}
	for _, forbidden := range []string{"actions/checkout@", "go build", "npm ", "wails build", "./scripts/", "./eng/"} {
		if strings.Contains(publish, forbidden) {
			t.Errorf("publish executes/rebuilds repository: %s", forbidden)
		}
	}
	want := []string{
		"actions/attest-build-provenance@4d101475d8b20a2381f78447822ac1eab6504dd8",
		"actions/attest-sbom@c604332985a26aa8cf1bdc465b92731239ec6b9e",
		"actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
		"actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c",
		"actions/setup-go@924ae3a1cded613372ab5595356fb5720e22ba16",
		"actions/setup-node@820762786026740c76f36085b0efc47a31fe5020",
		"actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
	}
	got := []string{}
	for _, match := range regexp.MustCompile(`(?m)^\s*uses:\s+(\S+)`).FindAllStringSubmatch(w, -1) {
		got = append(got, match[1])
	}
	slices.Sort(want)
	slices.Sort(got)
	if !slices.Equal(want, got) {
		t.Fatal("release action pin drift", got)
	}
}

func TestBuildRetainsLockedChecksAndPortableVersionContract(t *testing.T) {
	if !strings.Contains(repositoryText(t, ".gitattributes"), "frontend/** text=auto eol=lf") {
		t.Fatal("frontend checkout must preserve formatter line endings")
	}
	script := repositoryText(t, "scripts/build-windows.ps1")
	for _, command := range []string{"./scripts/validate-go.ps1", "ci --ignore-scripts --no-audit --no-fund", "wails/v2/cmd/wails@v2.15.0", "-webview2 error", "-platform windows/amd64", "-trimpath", "go vet .", "go test . -count=1", "./scripts/smoke-windows.ps1"} {
		if !strings.Contains(script, command) {
			t.Errorf("missing build gate %s", command)
		}
	}
	var config struct {
		Info struct {
			ProductVersion string `json:"productVersion"`
		}
		Build string `json:"frontend:build"`
	}
	if json.Unmarshal([]byte(repositoryText(t, "wails.json")), &config) != nil || config.Info.ProductVersion != "1.0.0" || config.Build != "npm run validate" {
		t.Fatal("portable version/build contract drift")
	}
	var pkg struct {
		Version, PackageManager string
		Scripts                 map[string]string
	}
	if json.Unmarshal([]byte(repositoryText(t, "frontend/package.json")), &pkg) != nil || pkg.Version != config.Info.ProductVersion || pkg.PackageManager != "npm@"+strings.TrimSpace(repositoryText(t, ".npm-version")) || pkg.Scripts["validate"] != "npm run check && npm test && npm run build" {
		t.Fatal("frontend pin/check drift")
	}
	if !strings.Contains(repositoryText(t, "internal/desktop/engine.go"), `const Version = "1.0.0"`) {
		t.Fatal("runtime version drift")
	}
	for _, gate := range []string{"WaitForExit(55000)", "ExitCode -ne 0", "Go desktop smoke: PASS"} {
		if !strings.Contains(repositoryText(t, "scripts/smoke-windows.ps1"), gate) {
			t.Fatal("smoke exit/identity gate missing")
		}
	}
}

func TestCoreDoesNotDependOnDesktopUIAndNativeEffectsStayScoped(t *testing.T) {
	err := filepath.WalkDir("..", func(path string, entry fs.DirEntry, err error) error {
		if err != nil {
			return err
		}
		if entry.IsDir() || !strings.HasSuffix(path, ".go") || strings.HasSuffix(path, "_test.go") {
			return nil
		}
		file, err := parser.ParseFile(token.NewFileSet(), path, nil, parser.ImportsOnly)
		if err != nil {
			return err
		}
		for _, spec := range file.Imports {
			name, _ := strconv.Unquote(spec.Path.Value)
			if strings.Contains(name, "wails") || strings.Contains(name, "/internal/desktop") {
				t.Errorf("UI dependency in core %s: %s", path, name)
			}
			if name == "os/exec" && !slices.Contains([]string{"lifecycle", "resources", "background"}, file.Name.Name) {
				t.Errorf("unscoped process creation %s", path)
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
}
