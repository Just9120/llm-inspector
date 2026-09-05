-- Migrated verbatim from the reviewed .NET schema v1. Keep existing databases compatible.
CREATE TABLE IF NOT EXISTS schema_migrations(
    version INTEGER PRIMARY KEY,
    applied_at_utc TEXT NOT NULL
) STRICT;

CREATE TABLE IF NOT EXISTS history_settings(
    id INTEGER PRIMARY KEY CHECK(id = 1),
    retention INTEGER NOT NULL CHECK(retention BETWEEN 0 AND 3)
) STRICT;

INSERT INTO history_settings(id, retention) VALUES(1, 1)
ON CONFLICT(id) DO NOTHING;

CREATE TABLE IF NOT EXISTS sessions(
    session_id TEXT PRIMARY KEY CHECK(length(session_id) = 32),
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    client INTEGER NOT NULL,
    backend INTEGER NOT NULL,
    model TEXT NULL CHECK(model IS NULL OR length(model) <= 128)
) STRICT;

CREATE TABLE IF NOT EXISTS operations(
    operation_id TEXT PRIMARY KEY CHECK(length(operation_id) = 32),
    session_id TEXT NULL REFERENCES sessions(session_id) ON DELETE SET NULL,
    started_at_utc TEXT NOT NULL,
    ended_at_utc TEXT NULL,
    client INTEGER NOT NULL,
    backend INTEGER NOT NULL,
    model TEXT NULL CHECK(model IS NULL OR length(model) <= 128),
    status INTEGER NOT NULL,
    error_type INTEGER NOT NULL
) STRICT;

CREATE TABLE IF NOT EXISTS requests(
    request_id TEXT PRIMARY KEY CHECK(length(request_id) = 32),
    session_id TEXT NULL REFERENCES sessions(session_id) ON DELETE SET NULL,
    operation_id TEXT NULL REFERENCES operations(operation_id) ON DELETE SET NULL,
    started_at_utc TEXT NOT NULL,
    http_status_code INTEGER NULL,
    outcome INTEGER NOT NULL,
    error_type INTEGER NOT NULL,
    client INTEGER NOT NULL,
    backend INTEGER NOT NULL,
    model TEXT NULL CHECK(model IS NULL OR length(model) <= 128)
) STRICT;

CREATE TABLE IF NOT EXISTS request_metrics(
    request_id TEXT NOT NULL REFERENCES requests(request_id) ON DELETE CASCADE,
    metric_key TEXT NOT NULL CHECK(metric_key IN (
        'input_tokens', 'output_tokens', 'total_tokens', 'cached_tokens', 'reasoning_tokens',
        'context_usage_tokens', 'context_limit_tokens', 'context_history_tokens', 'context_tool_tokens',
        'prompt_tokens_per_second', 'generation_tokens_per_second', 'ttft_ms',
        'model_load_ms', 'queue_ms', 'total_duration_ms')),
    value REAL NULL,
    unit INTEGER NOT NULL,
    quality INTEGER NOT NULL,
    source INTEGER NOT NULL,
    source_version TEXT NOT NULL CHECK(length(source_version) BETWEEN 1 AND 128),
    derivation_version TEXT NULL CHECK(derivation_version IS NULL OR length(derivation_version) <= 128),
    PRIMARY KEY(request_id, metric_key)
) STRICT;

CREATE TABLE IF NOT EXISTS turns(
    turn_id TEXT PRIMARY KEY CHECK(length(turn_id) = 32),
    operation_id TEXT NOT NULL REFERENCES operations(operation_id) ON DELETE CASCADE,
    sequence INTEGER NOT NULL CHECK(sequence >= 0),
    request_id TEXT NULL REFERENCES requests(request_id) ON DELETE SET NULL,
    started_at_utc TEXT NOT NULL,
    duration_ms REAL NOT NULL CHECK(duration_ms >= 0),
    outcome INTEGER NOT NULL,
    error_type INTEGER NOT NULL,
    UNIQUE(operation_id, sequence)
) STRICT;

CREATE TABLE IF NOT EXISTS tool_events(
    tool_event_id TEXT PRIMARY KEY CHECK(length(tool_event_id) = 32),
    operation_id TEXT NOT NULL REFERENCES operations(operation_id) ON DELETE CASCADE,
    turn_sequence INTEGER NOT NULL CHECK(turn_sequence >= 0),
    sequence INTEGER NOT NULL CHECK(sequence >= 0),
    tool_name TEXT NOT NULL CHECK(length(tool_name) BETWEEN 1 AND 128),
    started_at_utc TEXT NOT NULL,
    duration_ms REAL NOT NULL CHECK(duration_ms >= 0),
    status INTEGER NOT NULL,
    error_type INTEGER NOT NULL,
    UNIQUE(operation_id, turn_sequence, sequence)
) STRICT;

CREATE TABLE IF NOT EXISTS resource_samples(
    sample_id TEXT PRIMARY KEY CHECK(length(sample_id) = 32),
    operation_id TEXT NULL REFERENCES operations(operation_id) ON DELETE CASCADE,
    captured_at_utc TEXT NOT NULL,
    cpu_percent REAL NULL CHECK(cpu_percent IS NULL OR cpu_percent BETWEEN 0 AND 100),
    cpu_quality INTEGER NOT NULL,
    cpu_source INTEGER NOT NULL,
    cpu_source_version TEXT NOT NULL CHECK(length(cpu_source_version) BETWEEN 1 AND 128),
    cpu_derivation_version TEXT NULL CHECK(cpu_derivation_version IS NULL OR length(cpu_derivation_version) BETWEEN 1 AND 128),
    memory_percent REAL NULL CHECK(memory_percent IS NULL OR memory_percent BETWEEN 0 AND 100),
    memory_quality INTEGER NOT NULL,
    memory_source INTEGER NOT NULL,
    memory_source_version TEXT NOT NULL CHECK(length(memory_source_version) BETWEEN 1 AND 128),
    memory_derivation_version TEXT NULL CHECK(memory_derivation_version IS NULL OR length(memory_derivation_version) BETWEEN 1 AND 128)
) STRICT;

CREATE INDEX IF NOT EXISTS ix_requests_period ON requests(started_at_utc);
CREATE INDEX IF NOT EXISTS ix_requests_filters
    ON requests(client, backend, model, session_id, outcome, error_type, started_at_utc);
CREATE INDEX IF NOT EXISTS ix_operations_session ON operations(session_id, started_at_utc);
CREATE INDEX IF NOT EXISTS ix_turns_operation ON turns(operation_id, sequence);
CREATE INDEX IF NOT EXISTS ix_tool_events_operation ON tool_events(operation_id, turn_sequence, sequence);
CREATE INDEX IF NOT EXISTS ix_resource_samples_period ON resource_samples(captured_at_utc);

