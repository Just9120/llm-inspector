package history

import (
	"context"
	"database/sql"

	"github.com/Just9120/llm-inspector/internal/domain"
)

func validateResource(r *domain.ResourceSample) error {
	if id(r.ID) == "" || !validOptionalID(r.RequestID) || !validOptionalID(r.OperationID) || r.CapturedAt.IsZero() || r.DroppedSamples < 0 || !validOptionalIdentifier(r.GPUDeviceID) || !validOptionalIdentifier(r.GPUDriverVersion) || (r.Stage != nil && !r.Stage.Valid()) || (r.Process != nil && !r.Process.Valid()) {
		return ErrInvalid
	}
	for _, f := range resourceFields(r) {
		if _, err := metricArgs(*f.value, f.unit); err != nil {
			return err
		}
	}
	return nil
}

func (s *Store) RecordResources(ctx context.Context, samples []domain.ResourceSample) error {
	if len(samples) > MaxResources {
		return ErrTooLarge
	}
	for i := range samples {
		if err := validateResource(&samples[i]); err != nil {
			return err
		}
	}
	return s.write(ctx, func(tx *sql.Tx) error {
		for i := range samples {
			if err := recordResource(ctx, tx, &samples[i]); err != nil {
				return err
			}
		}
		return nil
	})
}

func recordResource(ctx context.Context, tx *sql.Tx, r *domain.ResourceSample) error {
	cols := []string{"sample_id", "operation_id", "request_id", "captured_at_utc", "gpu_device_id", "dropped_sample_count"}
	args := []any{id(r.ID), nullable(id(r.OperationID)), nullable(id(r.RequestID)), dbTime(r.CapturedAt), nullable(r.GPUDeviceID), r.DroppedSamples}
	for _, pair := range []struct {
		key string
		m   domain.Metric
	}{{"cpu", r.CPU}, {"memory", r.MemoryPercent}} {
		cols = append(cols, pair.key+"_percent", pair.key+"_quality", pair.key+"_source", pair.key+"_source_version", pair.key+"_derivation_version")
		args = append(args, pair.m.Value, code(qualities, pair.m.Quality), code(sources, pair.m.Source), pair.m.SourceVersion, nullable(pair.m.DerivationVersion))
	}
	if r.Stage != nil {
		cols = append(cols, "stage", "stage_evidence", "stage_source_version")
		args = append(args, code(stages, r.Stage.Stage), code(stageEvidence, r.Stage.Evidence), r.Stage.SourceVersion)
	}
	if p := r.Process; p != nil {
		cols = append(cols, "process_id", "process_started_at_utc", "process_image_name", "process_association_source_version")
		args = append(args, p.PID, dbTime(p.StartedAt), p.ImageName, p.SourceVersion)
	}
	if err := insert(ctx, tx, "resource_samples", cols, args, ""); err != nil {
		return err
	}
	for _, f := range resourceFields(r) {
		if err := putMetric(ctx, tx, "resource_sample_metrics", "sample_id", id(r.ID), f.key, *f.value, f.unit); err != nil {
			return err
		}
	}
	return nil
}
