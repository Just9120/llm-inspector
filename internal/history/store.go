// Package history persists a closed technical allowlist in the existing v5 SQLite database.
package history

import (
	"context"
	"database/sql"
	"embed"
	"errors"
	"fmt"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"time"

	_ "modernc.org/sqlite"
)

//go:embed schema/*.sql
var schemas embed.FS

var (
	ErrInvalid      = errors.New("некорректные технические данные или область истории")
	ErrSchema       = errors.New("неподдерживаемая версия схемы истории")
	ErrIntegrity    = errors.New("проверка целостности истории не пройдена")
	ErrConfirmation = errors.New("очистка истории требует подтверждения выбранной области")
	ErrTooLarge     = errors.New("выборка слишком велика; сузьте период")
)

const SchemaVersion = 5
const MaxRequests = 1000
const MaxResources = 5000

type Store struct{ writer, reader *sql.DB }

// Open does not recover by deleting/recreating a damaged or newer database.
// The shell owns the per-user single-instance boundary. This store serializes
// application writes while WAL readers use their own read-only connections.
func Open(ctx context.Context, path string) (*Store, error) {
	if strings.TrimSpace(path) == "" {
		return nil, ErrInvalid
	}
	abs, err := filepath.Abs(path)
	if err != nil {
		return nil, err
	}
	if err = os.MkdirAll(filepath.Dir(abs), 0700); err != nil {
		return nil, err
	}
	u := url.URL{Scheme: "file", Path: filepath.ToSlash(abs)}
	if u.Path[0] != '/' {
		u.Path = "/" + u.Path
	}
	q := url.Values{"_pragma": {"busy_timeout(5000)", "foreign_keys(1)"}}
	q.Set("mode", "rwc")
	q.Set("_txlock", "immediate")
	u.RawQuery = q.Encode()
	w, err := sql.Open("sqlite", u.String())
	if err != nil {
		return nil, err
	}
	w.SetMaxOpenConns(1)
	w.SetMaxIdleConns(1)
	s := &Store{writer: w}
	if err = s.initialize(ctx); err != nil {
		w.Close()
		return nil, err
	}
	q.Set("mode", "ro")
	q.Del("_txlock")
	u.RawQuery = q.Encode()
	s.reader, err = sql.Open("sqlite", u.String())
	if err != nil {
		w.Close()
		return nil, err
	}
	s.reader.SetMaxOpenConns(4)
	s.reader.SetMaxIdleConns(2)
	if err = s.reader.PingContext(ctx); err != nil {
		s.Close()
		return nil, err
	}
	return s, nil
}

func (s *Store) initialize(ctx context.Context) error {
	var result string
	if err := s.writer.QueryRowContext(ctx, "PRAGMA quick_check(1)").Scan(&result); err != nil || result != "ok" {
		return ErrIntegrity
	}
	// Inspect the version before any schema/WAL mutation of an unknown file.
	var exists int
	if err := s.writer.QueryRowContext(ctx, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations'").Scan(&exists); err != nil {
		return err
	}
	version := 0
	if exists > 0 {
		if err := s.writer.QueryRowContext(ctx, "SELECT COALESCE(MAX(version),0) FROM schema_migrations").Scan(&version); err != nil {
			return err
		}
		if version < 0 || version > SchemaVersion {
			return ErrSchema
		}
	}
	if err := s.writer.QueryRowContext(ctx, "PRAGMA journal_mode=WAL").Scan(&result); err != nil {
		return err
	}
	if result != "wal" {
		return ErrIntegrity
	}
	return s.write(ctx, func(tx *sql.Tx) error {
		for next := version + 1; next <= SchemaVersion; next++ {
			body, err := schemas.ReadFile(fmt.Sprintf("schema/%d.sql", next))
			if err != nil {
				return err
			}
			if _, err = tx.ExecContext(ctx, string(body)); err != nil {
				return err
			}
			if _, err = tx.ExecContext(ctx, "INSERT INTO schema_migrations(version,applied_at_utc) VALUES(?,?)", next, dbTime(time.Now())); err != nil {
				return err
			}
		}
		return nil
	})
}

func (s *Store) Close() error {
	var r error
	if s.reader != nil {
		r = s.reader.Close()
	}
	return errors.Join(r, s.writer.Close())
}

func (s *Store) write(ctx context.Context, fn func(*sql.Tx) error) error {
	tx, err := s.writer.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	if err = fn(tx); err != nil {
		return err
	}
	return tx.Commit()
}

// .NET's UTC "O" format uses exactly seven fraction digits and +00:00.
// Keeping this representation is essential for existing lexical range indexes.
func dbTime(t time.Time) string             { return t.UTC().Format("2006-01-02T15:04:05.0000000+00:00") }
func parseTime(s string) (time.Time, error) { return time.Parse(time.RFC3339Nano, s) }
func nullable(s string) any {
	if s == "" {
		return nil
	}
	return s
}
func nullableTime(t *time.Time) any {
	if t == nil {
		return nil
	}
	return dbTime(*t)
}

func insert(ctx context.Context, tx *sql.Tx, table string, columns []string, args []any, suffix string) error {
	// Only package-owned constant identifiers reach this helper; values are bound.
	marks := strings.TrimSuffix(strings.Repeat("?,", len(args)), ",")
	_, err := tx.ExecContext(ctx, "INSERT INTO "+table+"("+strings.Join(columns, ",")+") VALUES("+marks+") "+suffix, args...)
	return err
}
