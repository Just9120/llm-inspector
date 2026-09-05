package history

import (
	"context"
	"database/sql"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func validateObservation(o *domain.Observation) error {
	if id(o.RequestID) == "" || o.StartedAt.IsZero() || !finiteDuration(o.DurationMS) || code(outcomes, o.Outcome) < 0 || code(errorsList, o.ErrorType) < 0 || code(origins, o.ErrorOrigin) < 0 || code(clients, o.Client) < 0 || code(backends, o.Telemetry.Backend) < 0 || code(loads, o.Telemetry.ModelLoad) < 0 || !validOptionalIdentifier(o.Telemetry.Model) || !o.Runtime.Valid() {
		return ErrInvalid
	}
	if o.HTTPStatus != nil && (*o.HTTPStatus < 100 || *o.HTTPStatus > 599) {
		return ErrInvalid
	}
	if c := o.Correlation; c != nil && (id(c.SessionID) == "" || id(c.TurnID) == "" || c.Sequence < 1 || !validOptionalID(c.OperationID)) {
		return ErrInvalid
	}
	for _, f := range requestFields(o) {
		if _, err := metricArgs(*f.value, f.unit); err != nil {
			return err
		}
	}
	return nil
}

// Record commits request and the optional operation update atomically. All
// projections are validated before entering the transaction; raw JSON has no API.
func (s *Store) Record(ctx context.Context, o domain.Observation) error {
	if err := validateObservation(&o); err != nil {
		return err
	}
	if o.Operation != nil {
		if err := validateGraph(o.Operation); err != nil {
			return err
		}
	}
	return s.write(ctx, func(tx *sql.Tx) error {
		if err := recordRequest(ctx, tx, &o); err != nil {
			return err
		}
		if o.Operation != nil {
			return recordGraph(ctx, tx, o.Operation)
		}
		return nil
	})
}

func recordRequest(ctx context.Context, tx *sql.Tx, o *domain.Observation) error {
	var session, turn, seq any
	if c := o.Correlation; c != nil {
		session = id(c.SessionID)
		turn = id(c.TurnID)
		seq = c.Sequence
		end := o.StartedAt.Add(time.Duration(o.DurationMS * float64(time.Millisecond)))
		if err := upsertSession(ctx, tx, c.SessionID, o.StartedAt, &end, o.Client, o.Telemetry.Backend, o.Telemetry.Model); err != nil {
			return err
		}
	}
	cols := []string{"request_id", "session_id", "started_at_utc", "http_status_code", "outcome", "error_type", "error_origin", "client", "backend", "model", "correlation_turn_id", "correlation_turn_sequence", "model_load_disposition"}
	args := []any{id(o.RequestID), session, dbTime(o.StartedAt), o.HTTPStatus, code(outcomes, o.Outcome), code(errorsList, o.ErrorType), code(origins, o.ErrorOrigin), code(clients, o.Client), code(backends, o.Telemetry.Backend), nullable(o.Telemetry.Model), turn, seq, code(loads, o.Telemetry.ModelLoad)}
	if f := o.Runtime; f != nil {
		cols = append(cols, "runtime_configuration_id", "inspector_version", "framework_version", "operating_system_version", "telemetry_contract_version", "backend_version", "client_version", "model_version", "gpu_driver_version")
		args = append(args, f.ConfigurationID, nullable(f.InspectorVersion), nullable(f.FrameworkVersion), nullable(f.OSVersion), nullable(f.TelemetryVersion), nullable(f.BackendVersion), nullable(f.ClientVersion), nullable(f.ModelVersion), nullable(f.GPUDriverVersion))
	}
	if err := insert(ctx, tx, "requests", cols, args, ""); err != nil {
		return err
	}
	for _, f := range requestFields(o) {
		if err := putMetric(ctx, tx, "request_metrics", "request_id", id(o.RequestID), f.key, *f.value, f.unit); err != nil {
			return err
		}
	}
	duration := domain.Derived(o.DurationMS, domain.Milliseconds, domain.Calculated, "proxy-duration-v1", "monotonic-wall-duration-v1")
	return putMetric(ctx, tx, "request_metrics", "request_id", id(o.RequestID), "total_duration_ms", duration, domain.Milliseconds)
}

func putMetric(ctx context.Context, tx *sql.Tx, table, keyColumn, recordID, key string, m domain.Metric, unit domain.Unit) error {
	args, err := metricArgs(m, unit)
	if err != nil {
		return err
	}
	return insert(ctx, tx, table, []string{keyColumn, "metric_key", "value", "unit", "quality", "source", "source_version", "derivation_version"}, append([]any{recordID, key}, args...), "")
}

func upsertSession(ctx context.Context, tx *sql.Tx, session string, start time.Time, end *time.Time, client domain.Client, backend domain.Backend, model string) error {
	return insert(ctx, tx, "sessions", []string{"session_id", "started_at_utc", "ended_at_utc", "client", "backend", "model"}, []any{id(session), dbTime(start), nullableTime(end), code(clients, client), code(backends, backend), nullable(model)}, `ON CONFLICT(session_id) DO UPDATE SET
		started_at_utc=MIN(sessions.started_at_utc,excluded.started_at_utc),
		ended_at_utc=CASE WHEN sessions.ended_at_utc IS NULL THEN excluded.ended_at_utc WHEN excluded.ended_at_utc IS NULL THEN sessions.ended_at_utc ELSE MAX(sessions.ended_at_utc,excluded.ended_at_utc) END,
		client=excluded.client,backend=excluded.backend,model=excluded.model`)
}
