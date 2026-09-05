package history

import (
	"context"
	"database/sql"
	"strings"
	"time"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Filter struct {
	From        *time.Time     `json:"from,omitempty"`
	To          *time.Time     `json:"to,omitempty"`
	Client      domain.Client  `json:"client,omitempty"`
	Backend     domain.Backend `json:"backend,omitempty"`
	Model       string         `json:"model,omitempty"`
	SessionID   string         `json:"session_id,omitempty"`
	OperationID string         `json:"operation_id,omitempty"`
	Outcome     string         `json:"outcome,omitempty"`
	ErrorType   string         `json:"error_type,omitempty"`
	Limit       int            `json:"limit,omitempty"`
}

func (f Filter) validate() error {
	if f.From != nil && f.To != nil && f.From.After(*f.To) || !validOptionalID(f.SessionID) || !validOptionalID(f.OperationID) || !validOptionalIdentifier(f.Model) || f.Limit < 0 || f.Limit > MaxRequests {
		return ErrInvalid
	}
	if f.Client != "" && code(clients, f.Client) < 0 || f.Backend != "" && code(backends, f.Backend) < 0 || f.Outcome != "" && code(outcomes, f.Outcome) < 0 || f.ErrorType != "" && code(errorsList, f.ErrorType) < 0 {
		return ErrInvalid
	}
	return nil
}

func (f Filter) where() (string, []any) {
	conditions := []string{"1=1"}
	var args []any
	add := func(expr string, v any) { conditions = append(conditions, expr); args = append(args, v) }
	if f.From != nil {
		add("started_at_utc>=?", dbTime(*f.From))
	}
	if f.To != nil {
		add("started_at_utc<=?", dbTime(*f.To))
	}
	if f.Client != "" {
		add("client=?", code(clients, f.Client))
	}
	if f.Backend != "" {
		add("backend=?", code(backends, f.Backend))
	}
	if f.Model != "" {
		add("model=?", f.Model)
	}
	if f.SessionID != "" {
		add("session_id=?", id(f.SessionID))
	}
	if f.OperationID != "" {
		add("operation_id=?", id(f.OperationID))
	}
	if f.Outcome != "" {
		add("outcome=?", code(outcomes, f.Outcome))
	}
	if f.ErrorType != "" {
		add("error_type=?", code(errorsList, f.ErrorType))
	}
	return strings.Join(conditions, " AND "), args
}

type Request struct {
	domain.Observation
	SessionID        string                   `json:"session_id,omitempty"`
	OperationID      string                   `json:"operation_id,omitempty"`
	Metrics          map[string]domain.Metric `json:"metrics"`
	ErrorOccurrences int                      `json:"error_occurrences"`
}
type Requests struct {
	Items     []Request `json:"items"`
	Truncated bool      `json:"truncated"`
}
type queryer interface {
	QueryContext(context.Context, string, ...any) (*sql.Rows, error)
}

func (s *Store) Query(ctx context.Context, f Filter) (Requests, error) {
	if err := f.validate(); err != nil {
		return Requests{}, err
	}
	limit := f.Limit
	if limit == 0 {
		limit = 200
	}
	return queryRequests(ctx, s.reader, f, limit)
}

const requestSelect = `r.request_id,r.session_id,r.operation_id,r.started_at_utc,r.http_status_code,r.outcome,r.error_type,r.error_origin,r.client,r.backend,r.model,r.model_load_disposition,r.correlation_turn_id,r.correlation_turn_sequence,
r.runtime_configuration_id,r.inspector_version,r.framework_version,r.operating_system_version,r.telemetry_contract_version,r.backend_version,r.client_version,r.model_version,r.gpu_driver_version,
m.metric_key,m.value,m.unit,m.quality,m.source,m.source_version,m.derivation_version`

func queryRequests(ctx context.Context, q queryer, f Filter, limit int) (Requests, error) {
	where, args := f.where()
	args = append(args, limit+1)
	rows, err := q.QueryContext(ctx, "SELECT "+requestSelect+" FROM (SELECT * FROM requests WHERE "+where+" ORDER BY started_at_utc DESC,request_id LIMIT ?) r LEFT JOIN request_metrics m ON m.request_id=r.request_id ORDER BY r.started_at_utc DESC,r.request_id,m.metric_key", args...)
	if err != nil {
		return Requests{}, err
	}
	defer rows.Close()
	result := Requests{Items: []Request{}}
	last := ""
	for rows.Next() {
		var rid, start string
		var session, operation, model, turn *string
		var status, sequence *int
		var outcome, etype, origin, client, backend, load int
		var conf, app, framework, osv, contract, bv, cv, mv, gv *string
		var key, version, derivation *string
		var value *float64
		var unit, quality, source *int
		if err = rows.Scan(&rid, &session, &operation, &start, &status, &outcome, &etype, &origin, &client, &backend, &model, &load, &turn, &sequence, &conf, &app, &framework, &osv, &contract, &bv, &cv, &mv, &gv, &key, &value, &unit, &quality, &source, &version, &derivation); err != nil {
			return Requests{}, err
		}
		if rid != last {
			if len(result.Items) == limit {
				result.Truncated = true
				break
			}
			started, e := parseTime(start)
			if e != nil {
				return Requests{}, ErrInvalid
			}
			vals := []struct {
				list []string
				n    int
			}{{outcomes, outcome}, {errorsList, etype}, {origins, origin}, {clients, client}, {backends, backend}, {loads, load}}
			decoded := make([]string, len(vals))
			for i, v := range vals {
				decoded[i], err = decode(v.list, v.n)
				if err != nil {
					return Requests{}, err
				}
			}
			o := domain.Observation{RequestID: rid, StartedAt: started, HTTPStatus: status, Outcome: decoded[0], ErrorType: decoded[1], ErrorOrigin: decoded[2], Client: domain.Client(decoded[3]), Telemetry: domain.MissingTelemetry(domain.Backend(decoded[4])), TTFT: domain.Missing(domain.Milliseconds, "inspector", "history-v5"), ContextChange: domain.Missing(domain.TokenDelta, "inspector", "history-v5"), Agent: domain.MissingAgentTurn()}
			o.Telemetry.Model = deref(model)
			o.Telemetry.ModelLoad = decoded[5]
			if turn != nil && sequence != nil && session != nil {
				o.Correlation = &domain.Correlation{SessionID: *session, TurnID: *turn, Sequence: *sequence, OperationID: deref(operation)}
			}
			if conf != nil {
				o.Runtime = &domain.RuntimeFacts{ConfigurationID: *conf, InspectorVersion: deref(app), FrameworkVersion: deref(framework), OSVersion: deref(osv), TelemetryVersion: deref(contract), BackendVersion: deref(bv), ClientVersion: deref(cv), ModelVersion: deref(mv), GPUDriverVersion: deref(gv)}
			}
			if id(rid) == "" || !validOptionalID(deref(session)) || !validOptionalID(deref(operation)) || !validOptionalIdentifier(o.Telemetry.Model) || !o.Runtime.Valid() {
				return Requests{}, ErrInvalid
			}
			result.Items = append(result.Items, Request{Observation: o, SessionID: deref(session), OperationID: deref(operation), Metrics: map[string]domain.Metric{}})
			last = rid
		}
		if key != nil {
			if unit == nil || quality == nil || source == nil || version == nil {
				return Requests{}, ErrInvalid
			}
			m, e := decodeMetric(value, *unit, *quality, *source, *version, deref(derivation))
			if e != nil {
				return Requests{}, e
			}
			r := &result.Items[len(result.Items)-1]
			found := false
			if *key == "total_duration_ms" {
				if m.Unit != domain.Milliseconds {
					return Requests{}, ErrInvalid
				}
				if m.Value != nil {
					r.DurationMS = *m.Value
				}
				found = true
			}
			for _, field := range requestFields(&r.Observation) {
				if field.key == *key {
					if m.Unit != field.unit {
						return Requests{}, ErrInvalid
					}
					*field.value = m
					found = true
					break
				}
			}
			if !found {
				return Requests{}, ErrInvalid
			}
			r.Metrics[*key] = m
		}
	}
	if err = rows.Err(); err != nil {
		return Requests{}, err
	}
	// Missing legacy rows stay explicit; unavailable is never synthesized as zero.
	counts := map[string]int{}
	for _, r := range result.Items {
		if r.ErrorType != "none" {
			counts[r.ErrorType]++
		}
	}
	for i := range result.Items {
		r := &result.Items[i]
		r.ErrorOccurrences = counts[r.ErrorType]
		for _, f := range requestFields(&r.Observation) {
			if _, ok := r.Metrics[f.key]; !ok {
				r.Metrics[f.key] = *f.value
			}
		}
		if _, ok := r.Metrics["total_duration_ms"]; !ok {
			r.Metrics["total_duration_ms"] = domain.Missing(domain.Milliseconds, "inspector", "history-v5")
		}
	}
	return result, nil
}
func deref[T any](p *T) (v T) {
	if p != nil {
		return *p
	}
	return v
}
