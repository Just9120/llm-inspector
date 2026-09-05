//go:build windows

package main

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"

	"github.com/Just9120/llm-inspector/internal/validation"
)

func TestCLIRejectsUnsafePathsUnknownOptionsAndSecretEcho(t *testing.T) {
	for _, path := range []string{"", `relative.json`, `\\server\share\a.json`, `\\?\C:\a.json`, `C:\a.json:stream`, `C:relative.json`, `/tmp/a.json`} {
		if localPath(path) {
			t.Fatal("unsafe path", path)
		}
	}
	if !localPath(`C:\local\capture.json`) {
		t.Fatal("local path rejected")
	}
	for _, args := range [][]string{{}, {"--private-secret-canary=x"}, {"--pid=bad-private-secret-canary"}, {"unexpected-private-secret-canary"}} {
		var out, err bytes.Buffer
		if run(t.Context(), args, &out, &err) == 0 || bytes.Contains(err.Bytes(), []byte("private-secret-canary")) || out.Len() != 0 {
			t.Fatal("invalid CLI accepted or value leaked")
		}
	}
}

func TestCLICapturesExactExecutableAndNeverOverwritesReport(t *testing.T) {
	executable, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	f, err := os.Open(executable)
	if err != nil {
		t.Fatal(err)
	}
	h := sha256.New()
	_, err = io.Copy(h, f)
	f.Close()
	if err != nil {
		t.Fatal(err)
	}
	hash := hex.EncodeToString(h.Sum(nil))
	output := filepath.Join(t.TempDir(), "capture.json")
	args := []string{"--pid=" + strconv.Itoa(os.Getpid()), "--exe=" + executable,
		"--sha256=" + hash, "--source-sha=" + strings.Repeat("a", 40),
		"--duration=200ms", "--interval=200ms", "--output=" + output}
	var stdout, stderr bytes.Buffer
	if code := run(t.Context(), args, &stdout, &stderr); code != 0 {
		t.Fatal("capture failed", code, stderr.String(), stdout.String())
	}
	before, err := os.ReadFile(output)
	if err != nil {
		t.Fatal(err)
	}
	var report validation.Report
	if err := json.Unmarshal(before, &report); err != nil || report.ReleaseEvidence || report.ExecutableSHA != hash || report.TerminalStatus != "completed" || !report.Summary.Complete || len(report.Samples) < 2 {
		t.Fatal("invalid report", err)
	}
	if bytes.Contains(before, []byte(executable)) || bytes.Contains(before, []byte(output)) {
		t.Fatal("local paths leaked in report")
	}
	stdout.Reset()
	stderr.Reset()
	if code := run(t.Context(), args, &stdout, &stderr); code != 1 || stderr.String() != "capture_output_unavailable_or_exists\n" || stdout.Len() != 0 {
		t.Fatal("existing report not rejected", code)
	}
	after, err := os.ReadFile(output)
	if err != nil || !bytes.Equal(before, after) {
		t.Fatal("existing report changed")
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	cancelledOutput := filepath.Join(t.TempDir(), "cancelled.json")
	args[len(args)-1] = "--output=" + cancelledOutput
	stdout.Reset()
	stderr.Reset()
	if code := run(ctx, args, &stdout, &stderr); code != 2 {
		t.Fatal("cancelled capture must exit 2", code, stderr.String())
	}
	cancelled, err := os.ReadFile(cancelledOutput)
	if err != nil || json.Unmarshal(cancelled, &report) != nil || report.TerminalStatus != "cancelled" || report.ReleaseEvidence || report.Summary.Complete {
		t.Fatal("cancellation not recorded")
	}
}
