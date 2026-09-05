package history

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func testStore(t *testing.T) *Store {
	t.Helper()
	s, err := Open(t.Context(), filepath.Join(t.TempDir(), "история # test.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { s.Close() })
	return s
}
func observation(n int) domain.Observation {
	status := 200
	o := domain.Observation{RequestID: fmt.Sprintf("%032x", n), StartedAt: time.Date(2026, 9, 5, 10, 30, 0, 123456700, time.UTC).Add(time.Duration(n) * time.Second), DurationMS: 100.5, HTTPStatus: &status, Outcome: "completed", ErrorType: "none", ErrorOrigin: "not_applicable", Client: domain.Generic, Telemetry: domain.MissingTelemetry(domain.Ollama), TTFT: domain.Measured(12, domain.Milliseconds, "inspector", "test-v1"), ContextChange: domain.Missing(domain.TokenDelta, "inspector", "test-v1"), Agent: domain.MissingAgentTurn()}
	o.Telemetry.Model = "qwen3.5:9b"
	o.Telemetry.PromptTokens = domain.Measured(float64(n), domain.Tokens, "openai_usage", "test-v1")
	return o
}

func TestStoreRoundTripRestartAndPrivacy(t *testing.T) {
	path := filepath.Join(t.TempDir(), "history.db")
	s, err := Open(t.Context(), path)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { s.Close() })
	o := observation(1)
	o.Runtime = &domain.RuntimeFacts{ConfigurationID: "config-v1", BackendVersion: "0.9.1"}
	if err = s.Record(t.Context(), o); err != nil {
		t.Fatal(err)
	}
	if err = s.Close(); err != nil {
		t.Fatal(err)
	}
	s, err = Open(t.Context(), path)
	if err != nil {
		t.Fatal(err)
	}
	defer s.Close()
	if err = s.Record(t.Context(), observation(2)); err != nil {
		t.Fatal(err)
	}
	r, err := s.Query(t.Context(), Filter{From: &o.StartedAt, To: &o.StartedAt})
	if err != nil {
		t.Fatal(err)
	}
	if len(r.Items) != 1 || r.Truncated {
		t.Fatalf("bad selection %+v", r)
	}
	got := r.Items[0]
	if got.DurationMS != 100.5 || *got.Telemetry.PromptTokens.Value != 1 || got.Telemetry.QueueTime.Value != nil || got.Runtime == nil || got.Runtime.BackendVersion != "0.9.1" || !got.StartedAt.Equal(o.StartedAt) {
		t.Fatalf("bad roundtrip %+v", got)
	}
	for _, mutate := range []func(*domain.Observation){
		func(v *domain.Observation) { v.Telemetry.Model = "PRIVATE PROMPT text" },
		func(v *domain.Observation) { v.TTFT.SourceVersion = "PRIVATE PROMPT text" },
		func(v *domain.Observation) { v.ErrorType = "PRIVATE PROMPT text" },
		func(v *domain.Observation) { v.Runtime = &domain.RuntimeFacts{ConfigurationID: "PRIVATE PROMPT text"} },
		func(v *domain.Observation) { v.TTFT = domain.Measured(12, domain.Tokens, "inspector", "test") },
	} {
		v := observation(3)
		mutate(&v)
		if err := s.Record(t.Context(), v); !errors.Is(err, ErrInvalid) {
			t.Fatalf("invalid value accepted: %v", err)
		}
	}
	b, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if strings.Contains(string(b), "PRIVATE PROMPT") {
		t.Fatal("private metadata leaked")
	}
	if _, err = s.reader.ExecContext(t.Context(), "DELETE FROM requests"); err == nil {
		t.Fatal("reader can write")
	}
	var wal string
	if err = s.writer.QueryRow("PRAGMA journal_mode").Scan(&wal); err != nil || wal != "wal" {
		t.Fatal(wal, err)
	}
}

func TestSchemaMigrationCompatibilityAllVersions(t *testing.T) {
	for version := 1; version <= 5; version++ {
		t.Run(fmt.Sprint(version), func(t *testing.T) {
			path := filepath.Join(t.TempDir(), "legacy.db")
			db, err := sql.Open("sqlite", path)
			if err != nil {
				t.Fatal(err)
			}
			for v := 1; v <= version; v++ {
				body, _ := schemas.ReadFile(fmt.Sprintf("schema/%d.sql", v))
				if _, err = db.Exec(string(body)); err != nil {
					t.Fatal(err)
				}
				if _, err = db.Exec("INSERT INTO schema_migrations VALUES(?,?)", v, dbTime(time.Now())); err != nil {
					t.Fatal(err)
				}
			}
			start := "2026-09-05T10:30:01.1234567+00:00"
			if _, err = db.Exec(`INSERT INTO requests(request_id,started_at_utc,http_status_code,outcome,error_type,client,backend,model) VALUES('00000000000000000000000000000001',?,503,1,5,2,2,'legacy-model')`, start); err != nil {
				t.Fatal(err)
			}
			if _, err = db.Exec(`INSERT INTO request_metrics VALUES('00000000000000000000000000000001','input_tokens',42,0,0,0,'legacy-v1',NULL)`); err != nil {
				t.Fatal(err)
			}
			db.Close()
			s, err := Open(t.Context(), path)
			if err != nil {
				t.Fatal(err)
			}
			defer s.Close()
			at, _ := parseTime(start)
			result, err := s.Query(t.Context(), Filter{From: &at, To: &at})
			if err != nil {
				t.Fatal(err)
			}
			if len(result.Items) != 1 {
				t.Fatal("legacy row lost")
			}
			r := result.Items[0]
			if r.Client != domain.Hermes || r.Telemetry.Backend != domain.LMStudio || *r.Telemetry.PromptTokens.Value != 42 || r.Metrics["total_duration_ms"].Value != nil {
				t.Fatalf("legacy mapping %+v", r)
			}
			if version < 5 && r.ErrorOrigin != "model" {
				t.Fatal("v5 error-origin backfill missing")
			}
			if err = s.Record(t.Context(), observation(2)); err != nil {
				t.Fatal(err)
			}
			var v int
			s.writer.QueryRow("SELECT MAX(version) FROM schema_migrations").Scan(&v)
			if v != 5 {
				t.Fatal(v)
			}
		})
	}
}

func TestSchemaSQLMatchesReviewedReference(t *testing.T) {
	body, err := os.ReadFile("../../src/LlmInspector.Storage.Sqlite/SqliteTechnicalHistoryStore.cs")
	if err != nil {
		t.Fatal(err)
	}
	for i, name := range []string{"SchemaSql", "Migration2Sql", "Migration3Sql", "Migration4Sql", "Migration5Sql"} {
		_, tail, ok := strings.Cut(string(body), "private const string "+name+" = \"\"\"")
		if !ok {
			t.Fatal(name)
		}
		legacy, _, ok := strings.Cut(tail, "\"\"\";")
		if !ok {
			t.Fatal(name)
		}
		actual, _ := schemas.ReadFile(fmt.Sprintf("schema/%d.sql", i+1))
		_, sqlText, _ := strings.Cut(string(actual), "\n")
		if strings.Join(strings.Fields(legacy), " ") != strings.Join(strings.Fields(sqlText), " ") {
			t.Fatalf("schema v%d changed", i+1)
		}
	}
}

func TestNewerOrCorruptDatabaseFailsClosed(t *testing.T) {
	path := filepath.Join(t.TempDir(), "newer.db")
	s, err := Open(t.Context(), path)
	if err != nil {
		t.Fatal(err)
	}
	if _, err = s.writer.Exec("INSERT INTO schema_migrations VALUES(99,'future')"); err != nil {
		t.Fatal(err)
	}
	s.Close()
	before, _ := os.ReadFile(path)
	if _, err = Open(t.Context(), path); !errors.Is(err, ErrSchema) {
		t.Fatal(err)
	}
	after, _ := os.ReadFile(path)
	if string(before) != string(after) {
		t.Fatal("newer database changed")
	}
	broken := filepath.Join(t.TempDir(), "broken.db")
	if err = os.WriteFile(broken, []byte("not a database"), 0600); err != nil {
		t.Fatal(err)
	}
	if _, err = Open(t.Context(), broken); !errors.Is(err, ErrIntegrity) {
		t.Fatal(err)
	}
	data, _ := os.ReadFile(broken)
	if string(data) != "not a database" {
		t.Fatal("corrupt file replaced")
	}
}

func TestConcurrentWritesAndFiltersAreBounded(t *testing.T) {
	s := testStore(t)
	var wg sync.WaitGroup
	for i := 1; i <= 32; i++ {
		wg.Add(1)
		go func(n int) {
			defer wg.Done()
			if err := s.Record(t.Context(), observation(n)); err != nil {
				t.Error(err)
			}
		}(i)
	}
	wg.Wait()
	r, err := s.Query(t.Context(), Filter{Client: domain.Generic, Backend: domain.Ollama, Model: "qwen3.5:9b", Outcome: "completed", ErrorType: "none", Limit: 10})
	if err != nil {
		t.Fatal(err)
	}
	if len(r.Items) != 10 || !r.Truncated {
		t.Fatal("limit not enforced")
	}
	for _, f := range []Filter{{Limit: 1001}, {Client: "unknown"}, {SessionID: "bad"}, {Model: "secret text"}} {
		if _, err = s.Query(t.Context(), f); !errors.Is(err, ErrInvalid) {
			t.Fatal(err)
		}
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if err = s.Record(ctx, observation(99)); err == nil {
		t.Fatal("cancellation ignored")
	}
	all, err := s.Query(t.Context(), Filter{Limit: 1000})
	if err != nil || len(all.Items) != 32 {
		t.Fatal(len(all.Items), err)
	}
	if _, err = json.Marshal(all); err != nil {
		t.Fatal(err)
	}
}
