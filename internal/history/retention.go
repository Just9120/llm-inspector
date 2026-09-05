package history

import (
	"context"
	"crypto/sha256"
	"database/sql"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"strings"
	"time"
)

type Retention string

const (
	SevenDays  Retention = "7_days"
	ThirtyDays Retention = "30_days"
	NinetyDays Retention = "90_days"
	Indefinite Retention = "indefinite"
)

var retentionValues = []string{string(SevenDays), string(ThirtyDays), string(NinetyDays), string(Indefinite)}

func (s *Store) Retention(ctx context.Context) (Retention, error) {
	var n int
	if err := s.reader.QueryRowContext(ctx, "SELECT retention FROM history_settings WHERE id=1").Scan(&n); err != nil {
		return "", err
	}
	v, err := decode(retentionValues, n)
	return Retention(v), err
}
func (s *Store) SetRetention(ctx context.Context, r Retention) error {
	n := code(retentionValues, r)
	if n < 0 {
		return ErrInvalid
	}
	return s.write(ctx, func(tx *sql.Tx) error {
		_, err := tx.ExecContext(ctx, "UPDATE history_settings SET retention=? WHERE id=1", n)
		return err
	})
}

type ClearScope struct {
	All  bool       `json:"all"`
	From *time.Time `json:"from,omitempty"`
	To   *time.Time `json:"to,omitempty"`
}

func (s ClearScope) validate() error {
	if s.All && (s.From != nil || s.To != nil) || !s.All && s.From == nil && s.To == nil || s.From != nil && s.To != nil && s.From.After(*s.To) {
		return ErrInvalid
	}
	return nil
}

type ClearPreview struct {
	Scope  ClearScope     `json:"scope"`
	Counts map[string]int `json:"counts"`
	Token  string         `json:"token"`
}

var deletionTables = []string{"resource_samples", "tool_events", "turns", "requests", "operations", "sessions"}
var tableIDs = map[string]string{"resource_samples": "sample_id", "tool_events": "tool_event_id", "turns": "turn_id", "requests": "request_id", "operations": "operation_id", "sessions": "session_id"}

func timestamp(table, prefix string) string {
	if table == "resource_samples" {
		return prefix + "captured_at_utc"
	}
	if table == "operations" || table == "sessions" {
		return "COALESCE(" + prefix + "ended_at_utc," + prefix + "started_at_utc)"
	}
	return prefix + "started_at_utc"
}
func scopeWhere(scope ClearScope, table, prefix string) (string, []any) {
	if scope.All {
		return "1=1", nil
	}
	var parts []string
	var args []any
	if scope.From != nil {
		parts = append(parts, timestamp(table, prefix)+">=?")
		args = append(args, dbTime(*scope.From))
	}
	if scope.To != nil {
		parts = append(parts, timestamp(table, prefix)+"<=?")
		args = append(args, dbTime(*scope.To))
	}
	return strings.Join(parts, " AND "), args
}

// A parent with out-of-range CASCADE children is not eligible. Its structural
// anchor remains until those children age out; recent technical data is never
// deleted indirectly by an old request/operation. SET NULL references are safe.
func deletionWhere(scope ClearScope, table string) (string, []any) {
	where, args := scopeWhere(scope, table, table+".")
	var children []string
	if table == "requests" {
		children = []string{"resource_samples"}
	}
	if table == "operations" {
		children = []string{"resource_samples", "turns", "tool_events"}
	}
	for _, child := range children {
		cw, ca := scopeWhere(scope, child, "c.")
		where += " AND NOT EXISTS(SELECT 1 FROM " + child + " c WHERE c." + tableIDs[table] + "=" + table + "." + tableIDs[table] + " AND NOT (" + cw + "))"
		args = append(args, ca...)
	}
	return where, args
}

func (s *Store) PreviewClear(ctx context.Context, scope ClearScope) (ClearPreview, error) {
	if err := scope.validate(); err != nil {
		return ClearPreview{}, err
	}
	tx, err := s.reader.BeginTx(ctx, &sql.TxOptions{ReadOnly: true})
	if err != nil {
		return ClearPreview{}, err
	}
	defer tx.Rollback()
	p, err := previewClear(ctx, tx, scope)
	if err != nil {
		return ClearPreview{}, err
	}
	return p, tx.Commit()
}
func previewClear(ctx context.Context, q queryer, scope ClearScope) (ClearPreview, error) {
	h := sha256.New()
	encoded, _ := json.Marshal(scope)
	h.Write(encoded)
	p := ClearPreview{Scope: scope, Counts: map[string]int{}}
	for _, table := range deletionTables {
		where, args := deletionWhere(scope, table)
		rows, err := q.QueryContext(ctx, "SELECT "+tableIDs[table]+" FROM "+table+" WHERE "+where+" ORDER BY "+tableIDs[table], args...)
		if err != nil {
			return ClearPreview{}, err
		}
		p.Counts[table] = 0
		for rows.Next() {
			var rid string
			if err = rows.Scan(&rid); err != nil {
				rows.Close()
				return ClearPreview{}, err
			}
			fmt.Fprintf(h, "\n%s:%s", table, rid)
			p.Counts[table]++
		}
		err = rows.Err()
		rows.Close()
		if err != nil {
			return ClearPreview{}, err
		}
	}
	p.Token = hex.EncodeToString(h.Sum(nil))
	return p, nil
}

func (s *Store) Clear(ctx context.Context, p ClearPreview, confirmed bool) (ClearPreview, error) {
	if !confirmed {
		return ClearPreview{}, ErrConfirmation
	}
	if err := p.Scope.validate(); err != nil {
		return ClearPreview{}, err
	}
	var current ClearPreview
	err := s.write(ctx, func(tx *sql.Tx) error {
		var err error
		current, err = previewClear(ctx, tx, p.Scope)
		if err != nil {
			return err
		}
		if p.Token == "" || current.Token != p.Token {
			return ErrConfirmation
		}
		for _, table := range deletionTables {
			where, args := deletionWhere(p.Scope, table)
			if _, err = tx.ExecContext(ctx, "DELETE FROM "+table+" WHERE "+where, args...); err != nil {
				return err
			}
		}
		return nil
	})
	return current, err
}

// ApplyRetention deletes at most 500 root rows per transaction so collection
// can commit between batches. Cancellation leaves previously committed batches
// valid. Finite retention is strictly before cutoff; equal timestamps survive.
func (s *Store) ApplyRetention(ctx context.Context, r Retention, now time.Time) (int, error) {
	n := code(retentionValues, r)
	if n < 0 || now.IsZero() {
		return 0, ErrInvalid
	}
	if r == Indefinite {
		return 0, nil
	}
	days := []int{7, 30, 90}[n]
	cutoff := now.UTC().Add(-time.Duration(days) * 24 * time.Hour).Add(-time.Nanosecond).Truncate(100 * time.Nanosecond)
	scope := ClearScope{To: &cutoff}
	total := 0
	for _, table := range deletionTables {
		for {
			var count int64
			err := s.write(ctx, func(tx *sql.Tx) error {
				where, args := deletionWhere(scope, table)
				r, err := tx.ExecContext(ctx, "DELETE FROM "+table+" WHERE rowid IN (SELECT rowid FROM "+table+" WHERE "+where+" ORDER BY "+timestamp(table, "")+",rowid LIMIT 500)", args...)
				if err != nil {
					return err
				}
				count, err = r.RowsAffected()
				return err
			})
			total += int(count)
			if err != nil {
				return total, err
			}
			if count < 500 {
				break
			}
		}
	}
	return total, nil
}
