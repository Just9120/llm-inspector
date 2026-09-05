-- Migrated verbatim from the reviewed .NET schema v3. Keep existing databases compatible.
ALTER TABLE turns ADD COLUMN available_tool_count INTEGER NULL
    CHECK(available_tool_count IS NULL OR available_tool_count >= 0);
ALTER TABLE turns ADD COLUMN available_tool_count_quality INTEGER NOT NULL DEFAULT 3
    CHECK(available_tool_count_quality BETWEEN 0 AND 3);
ALTER TABLE turns ADD COLUMN available_tool_count_source INTEGER NOT NULL DEFAULT 2;
ALTER TABLE turns ADD COLUMN available_tool_count_source_version TEXT NOT NULL
    DEFAULT 'openai-agent-metadata-v1'
    CHECK(length(available_tool_count_source_version) BETWEEN 1 AND 128);
ALTER TABLE turns ADD COLUMN available_tool_count_derivation_version TEXT NULL
    CHECK(available_tool_count_derivation_version IS NULL OR length(available_tool_count_derivation_version) BETWEEN 1 AND 128);

ALTER TABLE turns ADD COLUMN invoked_tool_count INTEGER NULL
    CHECK(invoked_tool_count IS NULL OR invoked_tool_count >= 0);
ALTER TABLE turns ADD COLUMN invoked_tool_count_quality INTEGER NOT NULL DEFAULT 3
    CHECK(invoked_tool_count_quality BETWEEN 0 AND 3);
ALTER TABLE turns ADD COLUMN invoked_tool_count_source INTEGER NOT NULL DEFAULT 2;
ALTER TABLE turns ADD COLUMN invoked_tool_count_source_version TEXT NOT NULL
    DEFAULT 'openai-agent-metadata-v1'
    CHECK(length(invoked_tool_count_source_version) BETWEEN 1 AND 128);
ALTER TABLE turns ADD COLUMN invoked_tool_count_derivation_version TEXT NULL
    CHECK(invoked_tool_count_derivation_version IS NULL OR length(invoked_tool_count_derivation_version) BETWEEN 1 AND 128);

ALTER TABLE tool_events ADD COLUMN duration_quality INTEGER NOT NULL DEFAULT 1
    CHECK(duration_quality BETWEEN 0 AND 3);
ALTER TABLE tool_events ADD COLUMN duration_source INTEGER NOT NULL DEFAULT 2;
ALTER TABLE tool_events ADD COLUMN duration_source_version TEXT NOT NULL
    DEFAULT 'history-schema-v3-backfill'
    CHECK(length(duration_source_version) BETWEEN 1 AND 128);
ALTER TABLE tool_events ADD COLUMN duration_derivation_version TEXT NULL
    DEFAULT 'legacy-tool-duration-v1'
    CHECK(duration_derivation_version IS NULL OR length(duration_derivation_version) BETWEEN 1 AND 128);

