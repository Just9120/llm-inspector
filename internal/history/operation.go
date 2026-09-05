package history

import (
	"context"
	"database/sql"
	"strings"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func validateGraph(g *domain.OperationGraph) error {
	if id(g.ID) == "" || !validOptionalID(g.SessionID) || g.StartedAt.IsZero() || (g.EndedAt != nil && g.EndedAt.Before(g.StartedAt)) || code(clients, g.Client) < 0 || code(backends, g.Backend) < 0 || !validOptionalIdentifier(g.Model) || code(operationStatus, g.Status) < 0 || code(errorsList, g.ErrorType) < 0 || len(g.Turns) > 1024 || len(g.Tools) > 4096 {
		return ErrInvalid
	}
	turns := map[int]bool{}
	ids := map[string]bool{}
	for _, t := range g.Turns {
		if id(t.TurnID) == "" || !validOptionalID(t.RequestID) || t.Sequence < 0 || turns[t.Sequence] || ids[id(t.TurnID)] || t.StartedAt.IsZero() || !finiteDuration(t.DurationMS) || code(outcomes, t.Outcome) < 0 || code(errorsList, t.ErrorType) < 0 {
			return ErrInvalid
		}
		turns[t.Sequence] = true
		ids[id(t.TurnID)] = true
		if _, err := metricArgs(t.AvailableTools, domain.Count); err != nil {
			return err
		}
		if _, err := metricArgs(t.InvokedTools, domain.Count); err != nil {
			return err
		}
	}
	tools := map[[2]int]bool{}
	ids = map[string]bool{}
	for _, t := range g.Tools {
		key := [2]int{t.TurnSequence, t.Sequence}
		if id(t.ID) == "" || t.Sequence < 0 || t.TurnSequence < 0 || (!turns[t.TurnSequence] && !g.Truncated) || tools[key] || ids[id(t.ID)] || domain.TechnicalIdentifier(t.Name) == "" || t.StartedAt.IsZero() || code(toolStatus, t.Status) < 0 || code(errorsList, t.ErrorType) < 0 {
			return ErrInvalid
		}
		tools[key] = true
		ids[id(t.ID)] = true
		if _, err := metricArgs(t.Duration, domain.Milliseconds); err != nil {
			return err
		}
	}
	return nil
}

func (s *Store) RecordOperation(ctx context.Context, g domain.OperationGraph) error {
	if err := validateGraph(&g); err != nil {
		return err
	}
	return s.write(ctx, func(tx *sql.Tx) error { return recordGraph(ctx, tx, &g) })
}

func recordGraph(ctx context.Context, tx *sql.Tx, g *domain.OperationGraph) error {
	if g.SessionID != "" {
		if err := upsertSession(ctx, tx, g.SessionID, g.StartedAt, g.EndedAt, g.Client, g.Backend, g.Model); err != nil {
			return err
		}
	}
	if err := insert(ctx, tx, "operations", []string{"operation_id", "session_id", "started_at_utc", "ended_at_utc", "client", "backend", "model", "status", "error_type"}, []any{id(g.ID), nullable(id(g.SessionID)), dbTime(g.StartedAt), nullableTime(g.EndedAt), code(clients, g.Client), code(backends, g.Backend), nullable(g.Model), code(operationStatus, g.Status), code(errorsList, g.ErrorType)}, "ON CONFLICT(operation_id) DO UPDATE SET ended_at_utc=excluded.ended_at_utc,status=excluded.status,error_type=excluded.error_type"); err != nil {
		return err
	}
	for _, t := range g.Turns {
		requestID, err := existingID(ctx, tx, "requests", "request_id", t.RequestID)
		if err != nil {
			return err
		}
		cols := []string{"turn_id", "operation_id", "sequence", "request_id", "started_at_utc", "duration_ms", "outcome", "error_type"}
		args := []any{id(t.TurnID), id(g.ID), t.Sequence, requestID, dbTime(t.StartedAt), t.DurationMS, code(outcomes, t.Outcome), code(errorsList, t.ErrorType)}
		for _, pair := range []struct {
			key string
			m   domain.Metric
		}{{"available_tool_count", t.AvailableTools}, {"invoked_tool_count", t.InvokedTools}} {
			cols = append(cols, pair.key, pair.key+"_quality", pair.key+"_source", pair.key+"_source_version", pair.key+"_derivation_version")
			args = append(args, pair.m.Value, code(qualities, pair.m.Quality), code(sources, pair.m.Source), pair.m.SourceVersion, nullable(pair.m.DerivationVersion))
		}
		if err := insert(ctx, tx, "turns", cols, args, "ON CONFLICT(turn_id) DO NOTHING"); err != nil {
			return err
		}
		if t.RequestID != "" {
			if _, err := tx.ExecContext(ctx, "UPDATE requests SET operation_id=? WHERE request_id=? AND (operation_id IS NULL OR operation_id=?)", id(g.ID), id(t.RequestID), id(g.ID)); err != nil {
				return err
			}
		}
	}
	for _, t := range g.Tools {
		var duration float64
		if t.Duration.Value != nil {
			duration = *t.Duration.Value
		}
		cols := []string{"tool_event_id", "operation_id", "turn_sequence", "sequence", "tool_name", "started_at_utc", "duration_ms", "status", "error_type", "duration_quality", "duration_source", "duration_source_version", "duration_derivation_version"}
		args := []any{id(t.ID), id(g.ID), t.TurnSequence, t.Sequence, t.Name, dbTime(t.StartedAt), duration, code(toolStatus, t.Status), code(errorsList, t.ErrorType), code(qualities, t.Duration.Quality), code(sources, t.Duration.Source), t.Duration.SourceVersion, nullable(t.Duration.DerivationVersion)}
		var updates []string
		for _, c := range cols[6:] {
			updates = append(updates, c+"=excluded."+c)
		}
		if err := insert(ctx, tx, "tool_events", cols, args, "ON CONFLICT(tool_event_id) DO UPDATE SET "+strings.Join(updates, ",")); err != nil {
			return err
		}
	}
	return nil
}
