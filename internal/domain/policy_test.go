package domain

import (
	"os"
	"regexp"
	"strings"
	"testing"
)

func TestGoCIKeepsTrustBoundaries(t *testing.T) {
	b, err := os.ReadFile("../../.github/workflows/ci.yml")
	if err != nil {
		t.Fatal(err)
	}
	workflow := strings.ReplaceAll(string(b), "\r\n", "\n")
	for _, required := range []string{"\n  pull_request:\n", "\n  push:\n", "\npermissions:\n  contents: read\n", "name: windows-go", "go-version-file: .go-version", "node-version-file: .node-version", "persist-credentials: false", "./scripts/build-windows.ps1", "./eng/release/Test-ReleaseTools.ps1"} {
		if !strings.Contains(workflow, required) {
			t.Errorf("missing CI boundary %q", required)
		}
	}
	for _, forbidden := range []string{"pull_request_target", "contents: write", "id-token: write", "secrets.", "environment:", "continue-on-error:"} {
		if strings.Contains(workflow, forbidden) {
			t.Errorf("unsafe CI field %s", forbidden)
		}
	}
	uses := regexp.MustCompile(`(?m)^\s*uses:\s+(\S+)`).FindAllStringSubmatch(workflow, -1)
	approved := map[string]bool{
		"actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1":   true,
		"actions/setup-go@924ae3a1cded613372ab5595356fb5720e22ba16":   true,
		"actions/setup-node@820762786026740c76f36085b0efc47a31fe5020": true,
	}
	for _, match := range uses {
		if !approved[match[1]] {
			t.Error("unreviewed CI action")
		}
	}
	version, err := os.ReadFile("../../.go-version")
	if err != nil {
		t.Fatal(err)
	}
	mod, err := os.ReadFile("../../go.mod")
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(mod), "go "+strings.TrimSpace(string(version))+"\n") {
		t.Fatal("Go pin drift")
	}
}
