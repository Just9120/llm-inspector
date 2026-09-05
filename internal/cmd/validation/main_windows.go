//go:build windows

// Read-only validation CLI, never linked into the desktop executable.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"os/signal"
	"path/filepath"
	"runtime"
	"strings"
	"time"

	"github.com/Just9120/llm-inspector/internal/performance"
	"github.com/Just9120/llm-inspector/internal/validation"
)

func main() {
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	status := run(ctx, os.Args[1:], os.Stdout, os.Stderr)
	cancel()
	os.Exit(status)
}

func run(ctx context.Context, args []string, stdout, stderr io.Writer) int {
	flags := flag.NewFlagSet("inspector-capture", flag.ContinueOnError)
	flags.SetOutput(io.Discard) // invalid args may contain paths/credentials
	pid := flags.Uint("pid", 0, "exact running Inspector PID")
	executable := flags.String("exe", "", "exact local executable")
	hash := flags.String("sha256", "", "expected executable SHA-256")
	source := flags.String("source-sha", "", "source SHA associated with this artifact")
	profile := flags.String("profile", "balanced", "declared profile; capture does not change settings")
	phase := flags.String("phase", "pilot", "pilot, idle or active; declaration is not independent evidence")
	duration := flags.Duration("duration", 30*time.Second, "bounded measurement duration, max 2h")
	interval := flags.Duration("interval", time.Second, "sample interval, 200ms..10s")
	output := flags.String("output", "", "new local JSON file; existing file is never overwritten")
	if flags.Parse(args) != nil || flags.NArg() != 0 || *pid == 0 || uint64(*pid) > uint64(^uint32(0)) || !localPath(*output) || !localPath(*executable) {
		fmt.Fprintln(stderr, "invalid_capture_arguments")
		return 1
	}
	o := validation.Options{SourceSHA: *source, ExecutableSHA: *hash, Profile: performance.ProfileID(*profile), Phase: *phase, Duration: *duration, Interval: *interval, LogicalCPUs: runtime.NumCPU()}
	if o.Validate() != nil {
		fmt.Fprintln(stderr, "invalid_capture_identity_or_bounds")
		return 1
	}
	tree, err := validation.AttachProcessTree(uint32(*pid), *executable, *hash)
	if err != nil {
		fmt.Fprintln(stderr, "capture_target_unavailable_or_identity_mismatch")
		return 1
	}
	defer tree.Close()
	// Exclusive creation before sampling prevents a long run from overwriting an
	// earlier capture. An interrupted writer leaves a detectable incomplete file.
	f, err := os.OpenFile(*output, os.O_WRONLY|os.O_CREATE|os.O_EXCL, 0600)
	if err != nil {
		fmt.Fprintln(stderr, "capture_output_unavailable_or_exists")
		return 1
	}
	report, err := validation.Capture(ctx, o, tree)
	if err != nil {
		f.Close()
		fmt.Fprintln(stderr, "capture_initialization_failed")
		return 1
	}
	enc := json.NewEncoder(f)
	writeErr := enc.Encode(report)
	syncErr := f.Sync()
	closeErr := f.Close()
	if writeErr != nil || syncErr != nil || closeErr != nil {
		fmt.Fprintln(stderr, "capture_output_write_failed")
		return 1
	}
	summary := struct {
		Status          string             `json:"status"`
		Samples         int                `json:"samples"`
		ReleaseEvidence bool               `json:"release_evidence"`
		Summary         validation.Summary `json:"summary"`
	}{report.TerminalStatus, len(report.Samples), false, report.Summary}
	if json.NewEncoder(stdout).Encode(summary) != nil {
		return 1
	}
	if report.TerminalStatus != "completed" || !report.Summary.Complete {
		return 2
	}
	return 0
}

func localPath(path string) bool {
	// Lexical guard only: reject explicit UNC/device paths and alternate data
	// streams. The operator must also verify the drive/parent is local; mapped
	// drives and directory junctions are not a security boundary here.
	return len(path) > 3 && filepath.IsAbs(path) && path[1] == ':' && (path[2] == '\\' || path[2] == '/') && !strings.Contains(path[2:], ":") && !strings.HasPrefix(path, `\\`)
}
