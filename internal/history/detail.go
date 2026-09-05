package history

import (
	"context"
	"database/sql"
	"errors"

	"github.com/Just9120/llm-inspector/internal/domain"
)

type Slice struct {
	Requests           []Request               `json:"requests"`
	Resources          []domain.ResourceSample `json:"resource_samples"`
	RequestsTruncated  bool                    `json:"requests_truncated"`
	ResourcesTruncated bool                    `json:"resource_samples_truncated"`
}

// Slice uses one read transaction: request and resource previews cannot observe
// different revisions while background collection commits concurrently.
func (s *Store) Slice(ctx context.Context, f Filter) (Slice, error) {
	if err := f.validate(); err != nil {
		return Slice{}, err
	}
	tx, err := s.reader.BeginTx(ctx, &sql.TxOptions{ReadOnly: true})
	if err != nil {
		return Slice{}, err
	}
	defer tx.Rollback()
	reqs, err := queryRequests(ctx, tx, f, MaxRequests)
	if err != nil {
		return Slice{}, err
	}
	resources, truncated, err := queryResources(ctx, tx, f, MaxResources)
	if err != nil {
		return Slice{}, err
	}
	if err = tx.Commit(); err != nil {
		return Slice{}, err
	}
	return Slice{Requests: reqs.Items, Resources: resources, RequestsTruncated: reqs.Truncated, ResourcesTruncated: truncated}, nil
}

func queryResources(ctx context.Context, q queryer, f Filter, limit int) ([]domain.ResourceSample, bool, error) {
	where := "1=1"
	var args []any
	if f.From != nil {
		where += " AND captured_at_utc>=?"
		args = append(args, dbTime(*f.From))
	}
	if f.To != nil {
		where += " AND captured_at_utc<=?"
		args = append(args, dbTime(*f.To))
	}
	if f.OperationID != "" {
		where += " AND operation_id=?"
		args = append(args, id(f.OperationID))
	}
	if f.Client != "" || f.Backend != "" || f.Model != "" || f.SessionID != "" || f.Outcome != "" || f.ErrorType != "" {
		rw, ra := f.where()
		where += " AND request_id IN (SELECT request_id FROM requests WHERE " + rw + ")"
		args = append(args, ra...)
	}
	args = append(args, limit+1)
	rows, err := q.QueryContext(ctx, `SELECT r.sample_id,r.request_id,r.operation_id,r.captured_at_utc,r.gpu_device_id,r.dropped_sample_count,
r.stage,r.stage_evidence,r.stage_source_version,r.process_id,r.process_started_at_utc,r.process_image_name,r.process_association_source_version,
r.cpu_percent,r.cpu_quality,r.cpu_source,r.cpu_source_version,r.cpu_derivation_version,r.memory_percent,r.memory_quality,r.memory_source,r.memory_source_version,r.memory_derivation_version,
m.metric_key,m.value,m.unit,m.quality,m.source,m.source_version,m.derivation_version
FROM (SELECT * FROM resource_samples WHERE `+where+` ORDER BY captured_at_utc DESC,sample_id LIMIT ?) r LEFT JOIN resource_sample_metrics m ON m.sample_id=r.sample_id ORDER BY r.captured_at_utc DESC,r.sample_id,m.metric_key`, args...)
	if err != nil {
		return nil, false, err
	}
	defer rows.Close()
	result := []domain.ResourceSample{}
	last := ""
	truncated := false
	for rows.Next() {
		var sid, captured, cpuv, memv string
		var rid, oid, gpu, sv, ps, pi, pv, cpud, memd *string
		var dropped, cpuq, cpus, memq, mems int
		var stage, evidence, pid *int
		var cpu, mem *float64
		var key, version, derivation *string
		var value *float64
		var unit, quality, source *int
		if err = rows.Scan(&sid, &rid, &oid, &captured, &gpu, &dropped, &stage, &evidence, &sv, &pid, &ps, &pi, &pv, &cpu, &cpuq, &cpus, &cpuv, &cpud, &mem, &memq, &mems, &memv, &memd, &key, &value, &unit, &quality, &source, &version, &derivation); err != nil {
			return nil, false, err
		}
		if sid != last {
			if len(result) == limit {
				truncated = true
				break
			}
			r := domain.MissingResource()
			r.ID = sid
			r.RequestID = deref(rid)
			r.OperationID = deref(oid)
			r.GPUDeviceID = deref(gpu)
			r.DroppedSamples = dropped
			r.CapturedAt, err = parseTime(captured)
			if err != nil {
				return nil, false, ErrInvalid
			}
			r.CPU, err = decodeMetric(cpu, code(units, domain.Percent), cpuq, cpus, cpuv, deref(cpud))
			if err != nil {
				return nil, false, err
			}
			r.MemoryPercent, err = decodeMetric(mem, code(units, domain.Percent), memq, mems, memv, deref(memd))
			if err != nil {
				return nil, false, err
			}
			if stage != nil {
				if evidence == nil || sv == nil {
					return nil, false, ErrInvalid
				}
				st, e := decode(stages, *stage)
				if e != nil {
					return nil, false, e
				}
				ev, e := decode(stageEvidence, *evidence)
				if e != nil {
					return nil, false, e
				}
				r.Stage = &domain.StageValue{Stage: domain.Stage(st), Evidence: ev, SourceVersion: *sv}
			}
			if pid != nil {
				if ps == nil || pi == nil || pv == nil {
					return nil, false, ErrInvalid
				}
				at, e := parseTime(*ps)
				if e != nil {
					return nil, false, ErrInvalid
				}
				r.Process = &domain.ProcessAssociation{PID: *pid, StartedAt: at, ImageName: *pi, SourceVersion: *pv}
			}
			if err = validateResource(&r); err != nil {
				return nil, false, err
			}
			result = append(result, r)
			last = sid
		}
		if key != nil {
			if unit == nil || quality == nil || source == nil || version == nil {
				return nil, false, ErrInvalid
			}
			m, e := decodeMetric(value, *unit, *quality, *source, *version, deref(derivation))
			if e != nil {
				return nil, false, e
			}
			found := false
			for _, f := range resourceFields(&result[len(result)-1]) {
				if f.key == *key {
					if f.unit != m.Unit {
						return nil, false, ErrInvalid
					}
					*f.value = m
					found = true
					break
				}
			}
			if !found {
				return nil, false, ErrInvalid
			}
		}
	}
	return result, truncated, rows.Err()
}

type OperationDetail struct {
	Graph              domain.OperationGraph   `json:"graph"`
	Resources          []domain.ResourceSample `json:"resources"`
	ResourcesTruncated bool                    `json:"resources_truncated"`
}

func (s *Store) Operation(ctx context.Context, operationID string) (*OperationDetail, error) {
	if id(operationID) == "" {
		return nil, ErrInvalid
	}
	tx, err := s.reader.BeginTx(ctx, &sql.TxOptions{ReadOnly: true})
	if err != nil {
		return nil, err
	}
	defer tx.Rollback()
	g := domain.OperationGraph{ID: id(operationID), Turns: []domain.TurnRecord{}, Tools: []domain.ToolEvent{}}
	var session, model, end *string
	var start string
	var client, backend, status, etype int
	err = tx.QueryRowContext(ctx, "SELECT session_id,started_at_utc,ended_at_utc,client,backend,model,status,error_type FROM operations WHERE operation_id=?", g.ID).Scan(&session, &start, &end, &client, &backend, &model, &status, &etype)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	g.SessionID = deref(session)
	g.Model = deref(model)
	g.StartedAt, err = parseTime(start)
	if err != nil {
		return nil, ErrInvalid
	}
	if end != nil {
		at, e := parseTime(*end)
		if e != nil {
			return nil, ErrInvalid
		}
		g.EndedAt = &at
	}
	c, e := decode(clients, client)
	if e != nil {
		return nil, e
	}
	g.Client = domain.Client(c)
	b, e := decode(backends, backend)
	if e != nil {
		return nil, e
	}
	g.Backend = domain.Backend(b)
	g.Status, err = decode(operationStatus, status)
	if err != nil {
		return nil, err
	}
	g.ErrorType, err = decode(errorsList, etype)
	if err != nil {
		return nil, err
	}
	if err = readTurns(ctx, tx, &g); err != nil {
		return nil, err
	}
	if err = readTools(ctx, tx, &g); err != nil {
		return nil, err
	}
	// v5 has no truncation column. Hitting either collector bound is conservative
	// partial evidence, including when the legacy graph happens to equal the cap.
	g.Truncated = len(g.Turns) >= 1024 || len(g.Tools) >= 4096
	// Retention may remove a turn while its later tool event survives.
	turns := map[int]bool{}
	for _, turn := range g.Turns {
		turns[turn.Sequence] = true
	}
	for _, tool := range g.Tools {
		if !turns[tool.TurnSequence] {
			g.Truncated = true
		}
	}
	if err = validateGraph(&g); err != nil {
		return nil, err
	}
	resources, truncated, err := queryResources(ctx, tx, Filter{OperationID: g.ID}, MaxResources)
	if err != nil {
		return nil, err
	}
	if err = tx.Commit(); err != nil {
		return nil, err
	}
	return &OperationDetail{Graph: g, Resources: resources, ResourcesTruncated: truncated}, nil
}

func readTurns(ctx context.Context, tx *sql.Tx, g *domain.OperationGraph) error {
	rows, err := tx.QueryContext(ctx, `SELECT turn_id,request_id,sequence,started_at_utc,duration_ms,outcome,error_type,
available_tool_count,available_tool_count_quality,available_tool_count_source,available_tool_count_source_version,available_tool_count_derivation_version,
invoked_tool_count,invoked_tool_count_quality,invoked_tool_count_source,invoked_tool_count_source_version,invoked_tool_count_derivation_version FROM turns WHERE operation_id=? ORDER BY sequence LIMIT 1024`, g.ID)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var t domain.TurnRecord
		var rid *string
		var start, av, iv string
		var outcome, etype, aq, as, iq, is int
		var ac, ic *float64
		var ad, itd *string
		if err = rows.Scan(&t.TurnID, &rid, &t.Sequence, &start, &t.DurationMS, &outcome, &etype, &ac, &aq, &as, &av, &ad, &ic, &iq, &is, &iv, &itd); err != nil {
			return err
		}
		t.RequestID = deref(rid)
		t.StartedAt, err = parseTime(start)
		if err != nil {
			return ErrInvalid
		}
		t.Outcome, err = decode(outcomes, outcome)
		if err != nil {
			return err
		}
		t.ErrorType, err = decode(errorsList, etype)
		if err != nil {
			return err
		}
		t.AvailableTools, err = decodeMetric(ac, code(units, domain.Count), aq, as, av, deref(ad))
		if err != nil {
			return err
		}
		t.InvokedTools, err = decodeMetric(ic, code(units, domain.Count), iq, is, iv, deref(itd))
		if err != nil {
			return err
		}
		g.Turns = append(g.Turns, t)
	}
	return rows.Err()
}

func readTools(ctx context.Context, tx *sql.Tx, g *domain.OperationGraph) error {
	rows, err := tx.QueryContext(ctx, `SELECT tool_event_id,turn_sequence,sequence,tool_name,started_at_utc,duration_ms,status,error_type,duration_quality,duration_source,duration_source_version,duration_derivation_version FROM tool_events WHERE operation_id=? ORDER BY turn_sequence,sequence LIMIT 4096`, g.ID)
	if err != nil {
		return err
	}
	defer rows.Close()
	for rows.Next() {
		var t domain.ToolEvent
		var start, v string
		var value float64
		var status, etype, q, source int
		var d *string
		if err = rows.Scan(&t.ID, &t.TurnSequence, &t.Sequence, &t.Name, &start, &value, &status, &etype, &q, &source, &v, &d); err != nil {
			return err
		}
		t.StartedAt, err = parseTime(start)
		if err != nil {
			return ErrInvalid
		}
		t.Status, err = decode(toolStatus, status)
		if err != nil {
			return err
		}
		t.ErrorType, err = decode(errorsList, etype)
		if err != nil {
			return err
		}
		pv := &value
		if q == code(qualities, domain.Unavailable) {
			pv = nil
		}
		t.Duration, err = decodeMetric(pv, code(units, domain.Milliseconds), q, source, v, deref(d))
		if err != nil {
			return err
		}
		g.Tools = append(g.Tools, t)
	}
	return rows.Err()
}
