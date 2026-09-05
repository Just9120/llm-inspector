package history

import (
	"bufio"
	"context"
	"database/sql"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func TestProcessKillPreservesCommittedWAL(t *testing.T) {
	path := filepath.Join(t.TempDir(), "crash.db")
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(t.Context(), 15*time.Second)
	defer cancel()
	cmd := exec.CommandContext(ctx, exe, "-test.run=^TestWALCrashHelper$")
	cmd.Env = append(os.Environ(), "LLM_INSPECTOR_TEST_WAL_PATH="+path)
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		t.Fatal(err)
	}
	if err = cmd.Start(); err != nil {
		t.Fatal(err)
	}
	defer func() { cmd.Process.Kill(); cmd.Wait() }()
	scanner := bufio.NewScanner(stdout)
	if !scanner.Scan() || scanner.Text() != "COMMITTED" {
		t.Fatal("WAL helper did not commit")
	}
	if err = cmd.Process.Kill(); err != nil {
		t.Fatal(err)
	}
	cmd.Wait()
	s, err := Open(t.Context(), path)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	r, err := s.Query(t.Context(), Filter{})
	if err != nil || len(r.Items) != 1 || r.Items[0].RequestID != observation(1).RequestID {
		t.Fatal("committed data not recovered", err)
	}
	if err = s.Record(t.Context(), observation(3)); err != nil {
		t.Fatal(err)
	}
	r, err = s.Query(t.Context(), Filter{})
	if err != nil || len(r.Items) != 2 {
		t.Fatal("restart did not accept new record", err)
	}
}
func TestWALCrashHelper(t *testing.T) {
	path := os.Getenv("LLM_INSPECTOR_TEST_WAL_PATH")
	if path == "" {
		return
	}
	// This subprocess accesses only its parent's isolated test database.
	s, err := Open(context.Background(), path)
	if err != nil {
		t.Fatal(err)
	}
	if err = s.Record(context.Background(), observation(1)); err != nil {
		t.Fatal(err)
	}
	tx, err := s.writer.Begin()
	if err != nil {
		t.Fatal(err)
	}
	o := observation(2)
	if err = recordRequest(context.Background(), tx, &o); err != nil {
		t.Fatal(err)
	}
	fmt.Println("COMMITTED")
	<-time.After(30 * time.Second)
	t.Fatal("parent did not stop crash helper")
}

func TestBoundedWriterFlushAndFailureIsolation(t *testing.T) {
	s := testStore(t)
	b := NewBuffered(s)
	for i := 1; i <= 20; i++ {
		b.Observations() <- observation(i)
	}
	bad := observation(21)
	bad.ErrorType = "invalid"
	b.Observations() <- bad
	r := resource(1, observation(1).StartedAt)
	r.RequestID = observation(1).RequestID
	if !b.OfferResources([]domain.ResourceSample{r}) {
		t.Fatal("empty resource queue rejected")
	}
	ctx, cancel := context.WithTimeout(t.Context(), 10*time.Second)
	defer cancel()
	if err := b.Close(ctx); err != nil {
		t.Fatal(err)
	}
	h := b.Health()
	if h.Written != 21 || h.Failed != 1 {
		t.Fatal(h)
	}
	if b.OfferResources([]domain.ResourceSample{r}) {
		t.Fatal("closed sink accepted sample")
	}
	if b.Health().Dropped != 1 {
		t.Fatal("drop not recorded")
	}
	all, err := s.Query(t.Context(), Filter{})
	if err != nil || len(all.Items) != 20 {
		t.Fatal(err)
	}
	slice, err := s.Slice(t.Context(), Filter{})
	if err != nil || len(slice.Resources) != 1 || slice.Resources[0].RequestID != observation(1).RequestID {
		t.Fatal("terminal resource correlation lost", err)
	}
}

func TestLargeSelectionAndRetentionBatches(t *testing.T) {
	s := testStore(t)
	now := time.Date(2026, 9, 5, 12, 0, 0, 0, time.UTC)
	err := s.write(t.Context(), func(tx *sql.Tx) error {
		for i := 1; i <= 1001; i++ {
			o := observation(i)
			o.StartedAt = now.Add(-8 * 24 * time.Hour)
			if err := recordRequest(t.Context(), tx, &o); err != nil {
				return err
			}
		}
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	slice, err := s.Slice(t.Context(), Filter{})
	if err != nil || len(slice.Requests) != 1000 || !slice.RequestsTruncated {
		t.Fatal("truncation missing", err)
	}
	if _, err = s.Analyze(t.Context(), Filter{}); !errors.Is(err, ErrTooLarge) {
		t.Fatal("incomplete analytics allowed", err)
	}
	n, err := s.ApplyRetention(t.Context(), SevenDays, now)
	if err != nil || n != 1001 {
		t.Fatal(n, err)
	}
	r, err := s.Query(t.Context(), Filter{})
	if err != nil || len(r.Items) != 0 {
		t.Fatal(err)
	}
}

func TestDatabaseHasOnlyTechnicalTables(t *testing.T) {
	s := testStore(t)
	rows, err := s.reader.Query("SELECT name,sql FROM sqlite_master WHERE type='table'")
	if err != nil {
		t.Fatal(err)
	}
	defer rows.Close()
	count := 0
	for rows.Next() {
		var name, ddl string
		if err = rows.Scan(&name, &ddl); err != nil {
			t.Fatal(err)
		}
		count++
		for _, forbidden := range []string{"prompt_text", "response_text", "reasoning_text", "tool_arguments", "tool_results", "user_code", " BLOB"} {
			if strings.Contains(ddl, forbidden) {
				t.Fatal("non-technical schema", name)
			}
		}
	}
	if count != 10 {
		t.Fatal("schema table drift", count)
	}
}
