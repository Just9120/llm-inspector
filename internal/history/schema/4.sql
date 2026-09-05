-- Migrated verbatim from the reviewed .NET schema v4. Keep existing databases compatible.
ALTER TABLE resource_samples ADD COLUMN request_id TEXT NULL
    REFERENCES requests(request_id) ON DELETE CASCADE;
ALTER TABLE resource_samples ADD COLUMN stage INTEGER NULL
    CHECK(stage IS NULL OR stage BETWEEN 0 AND 7);
ALTER TABLE resource_samples ADD COLUMN stage_evidence INTEGER NULL
    CHECK(stage_evidence IS NULL OR stage_evidence BETWEEN 0 AND 1);
ALTER TABLE resource_samples ADD COLUMN stage_source_version TEXT NULL
    CHECK(stage_source_version IS NULL OR length(stage_source_version) BETWEEN 1 AND 128);
ALTER TABLE resource_samples ADD COLUMN process_id INTEGER NULL CHECK(process_id > 0);
ALTER TABLE resource_samples ADD COLUMN process_started_at_utc TEXT NULL;
ALTER TABLE resource_samples ADD COLUMN process_image_name TEXT NULL
    CHECK(process_image_name IS NULL OR length(process_image_name) BETWEEN 1 AND 128);
ALTER TABLE resource_samples ADD COLUMN process_association_source_version TEXT NULL
    CHECK(process_association_source_version IS NULL OR length(process_association_source_version) BETWEEN 1 AND 128);
ALTER TABLE resource_samples ADD COLUMN gpu_device_id TEXT NULL
    CHECK(gpu_device_id IS NULL OR length(gpu_device_id) BETWEEN 1 AND 128);
ALTER TABLE resource_samples ADD COLUMN dropped_sample_count INTEGER NOT NULL DEFAULT 0
    CHECK(dropped_sample_count >= 0);

CREATE TABLE resource_sample_metrics(
    sample_id TEXT NOT NULL REFERENCES resource_samples(sample_id) ON DELETE CASCADE,
    metric_key TEXT NOT NULL CHECK(metric_key IN (
        'system_cpu_percent', 'system_memory_percent', 'system_memory_used_bytes',
        'process_cpu_percent', 'process_memory_bytes', 'disk_read_bytes', 'disk_write_bytes',
        'client_to_backend_bytes', 'backend_to_client_bytes', 'gpu_utilization_percent',
        'gpu_vram_used_bytes', 'gpu_vram_total_bytes', 'gpu_temperature_celsius', 'gpu_power_watts')),
    value REAL NULL,
    unit INTEGER NOT NULL,
    quality INTEGER NOT NULL CHECK(quality BETWEEN 0 AND 3),
    source INTEGER NOT NULL,
    source_version TEXT NOT NULL CHECK(length(source_version) BETWEEN 1 AND 128),
    derivation_version TEXT NULL CHECK(derivation_version IS NULL OR length(derivation_version) BETWEEN 1 AND 128),
    PRIMARY KEY(sample_id, metric_key)
) STRICT;

CREATE INDEX ix_resource_samples_request ON resource_samples(request_id, captured_at_utc);
CREATE INDEX ix_resource_samples_operation_stage ON resource_samples(operation_id, stage, captured_at_utc);

