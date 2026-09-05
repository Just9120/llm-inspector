-- Migrated verbatim from the reviewed .NET schema v2. Keep existing databases compatible.
ALTER TABLE requests ADD COLUMN correlation_turn_id TEXT NULL
    CHECK(correlation_turn_id IS NULL OR length(correlation_turn_id) = 32);
ALTER TABLE requests ADD COLUMN correlation_turn_sequence INTEGER NULL
    CHECK(
        (correlation_turn_id IS NULL AND correlation_turn_sequence IS NULL) OR
        (correlation_turn_id IS NOT NULL AND correlation_turn_sequence >= 1));
ALTER TABLE requests ADD COLUMN model_load_disposition INTEGER NOT NULL DEFAULT 0
    CHECK(model_load_disposition BETWEEN 0 AND 2);
CREATE INDEX ix_requests_correlation
    ON requests(session_id, correlation_turn_sequence);

