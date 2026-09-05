-- Migrated verbatim from the reviewed .NET schema v5. Keep existing databases compatible.
ALTER TABLE requests ADD COLUMN error_origin INTEGER NOT NULL DEFAULT 1
    CHECK(error_origin BETWEEN 0 AND 5);
UPDATE requests SET error_origin = CASE
    WHEN error_type = 0 THEN 0
    WHEN error_type = 2 THEN 3
    WHEN error_type IN (5, 8) THEN 5
    WHEN error_type IN (1, 4, 6, 7, 9) THEN 4
    ELSE 1
END;

ALTER TABLE requests ADD COLUMN runtime_configuration_id TEXT NULL
    CHECK(runtime_configuration_id IS NULL OR length(runtime_configuration_id) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN inspector_version TEXT NULL
    CHECK(inspector_version IS NULL OR length(inspector_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN framework_version TEXT NULL
    CHECK(framework_version IS NULL OR length(framework_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN operating_system_version TEXT NULL
    CHECK(operating_system_version IS NULL OR length(operating_system_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN telemetry_contract_version TEXT NULL
    CHECK(telemetry_contract_version IS NULL OR length(telemetry_contract_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN backend_version TEXT NULL
    CHECK(backend_version IS NULL OR length(backend_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN client_version TEXT NULL
    CHECK(client_version IS NULL OR length(client_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN model_version TEXT NULL
    CHECK(model_version IS NULL OR length(model_version) BETWEEN 1 AND 128);
ALTER TABLE requests ADD COLUMN gpu_driver_version TEXT NULL
    CHECK(gpu_driver_version IS NULL OR length(gpu_driver_version) BETWEEN 1 AND 128);
CREATE INDEX ix_requests_runtime_configuration
    ON requests(runtime_configuration_id, started_at_utc);

