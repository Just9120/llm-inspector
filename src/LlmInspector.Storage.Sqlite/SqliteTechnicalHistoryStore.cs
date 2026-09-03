using System.Globalization;
using LlmInspector.Application;
using LlmInspector.Domain;
using Microsoft.Data.Sqlite;

namespace LlmInspector.Storage.Sqlite;

public sealed class SqliteTechnicalHistoryStore : ITechnicalHistoryStore, IAsyncDisposable
{
    private const int SchemaVersion = 4;
    private const int MaximumQueryLimit = 1_000;
    private readonly string _connectionString;
    private readonly string _readConnectionString;
    private readonly SemaphoreSlim _writerLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteTechnicalHistoryStore(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path is required.", nameof(databasePath));
        }

        string fullPath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
        _readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            SqliteConnectionStringBuilder builder = new(_connectionString);
            string? directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken)
                .ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, transaction, SchemaSql, cancellationToken).ConfigureAwait(false);
            int persistedVersion = await ReadSchemaVersionAsync(connection, transaction, cancellationToken)
                .ConfigureAwait(false);
            if (persistedVersion == 0)
            {
                await RecordSchemaVersionAsync(connection, transaction, 1, cancellationToken).ConfigureAwait(false);
                persistedVersion = 1;
            }

            if (persistedVersion > SchemaVersion)
            {
                throw new InvalidDataException(
                    $"History schema version {persistedVersion} is newer than supported version {SchemaVersion}.");
            }

            if (persistedVersion == 1)
            {
                await ExecuteNonQueryAsync(connection, transaction, Migration2Sql, cancellationToken)
                    .ConfigureAwait(false);
                await RecordSchemaVersionAsync(connection, transaction, 2, cancellationToken).ConfigureAwait(false);
                persistedVersion = 2;
            }

            if (persistedVersion == 2)
            {
                await ExecuteNonQueryAsync(connection, transaction, Migration3Sql, cancellationToken)
                    .ConfigureAwait(false);
                await RecordSchemaVersionAsync(connection, transaction, 3, cancellationToken).ConfigureAwait(false);
                persistedVersion = 3;
            }

            if (persistedVersion == 3)
            {
                await ExecuteNonQueryAsync(connection, transaction, Migration4Sql, cancellationToken)
                    .ConfigureAwait(false);
                await RecordSchemaVersionAsync(connection, transaction, 4, cancellationToken).ConfigureAwait(false);
                persistedVersion = 4;
            }

            if (persistedVersion != SchemaVersion)
            {
                throw new InvalidDataException(
                    $"History schema version {persistedVersion} has no supported migration path.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async ValueTask RecordAsync(
        ProxyObservation observation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await InsertRequestAsync(connection, transaction, observation, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task RecordOperationGraphAsync(
        TechnicalOperationGraph graph,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateOperationGraph(graph);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            if (graph.Session is not null)
            {
                await UpsertSessionAsync(connection, transaction, graph.Session, cancellationToken).ConfigureAwait(false);
            }

            await UpsertOperationAsync(connection, transaction, graph.Operation, cancellationToken).ConfigureAwait(false);
            foreach (TechnicalTurnRecord turn in graph.Turns.OrderBy(item => item.Sequence))
            {
                await InsertTurnAsync(connection, transaction, turn, cancellationToken).ConfigureAwait(false);
                if (turn.RequestId is Guid requestId)
                {
                    await LinkRequestAsync(
                        connection,
                        transaction,
                        requestId,
                        graph.Operation.SessionId,
                        graph.Operation.OperationId,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (TechnicalToolEventRecord tool in graph.ToolEvents
                         .OrderBy(item => item.TurnSequence)
                         .ThenBy(item => item.Sequence))
            {
                await InsertToolEventAsync(connection, transaction, tool, cancellationToken).ConfigureAwait(false);
            }

            foreach (TechnicalResourceSampleRecord sample in graph.ResourceSamples.OrderBy(item => item.CapturedAt))
            {
                await InsertResourceSampleAsync(connection, transaction, sample, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task RecordResourceSamplesAsync(
        IReadOnlyList<TechnicalResourceSampleRecord> samples,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0)
        {
            return;
        }

        if (samples.Select(item => item.SampleId).Distinct().Count() != samples.Count)
        {
            throw new ArgumentException("Resource sample identifiers must be unique within a batch.", nameof(samples));
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (TechnicalResourceSampleRecord sample in samples.OrderBy(item => item.CapturedAt))
            {
                await InsertResourceSampleAsync(connection, transaction, sample, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.Limit is < 1 or > MaximumQueryLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), $"History limit must be within 1..{MaximumQueryLimit}.");
        }

        return await QueryRequestsCoreAsync(filter, filter.Limit, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TechnicalOperationDetail?> GetOperationDetailAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        TechnicalOperationRecord? operation = await ReadOperationAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);
        if (operation is null)
        {
            return null;
        }

        IReadOnlyList<TechnicalTurnRecord> turns = await ReadTurnsAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TechnicalToolEventRecord> tools = await ReadToolsAsync(connection, operationId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TechnicalResourceSampleRecord> resources = await ReadResourcesAsync(
            connection,
            operationId,
            cancellationToken).ConfigureAwait(false);
        return new TechnicalOperationDetail(operation, turns, tools, resources);
    }

    public async Task<PeriodAnalytics> AnalyzePeriodAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        IReadOnlyList<RequestHistoryItem> requests = await QueryRequestsCoreAsync(filter, null, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<TechnicalResourceSampleRecord> resources = await QueryResourcesAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<DateOnly, Dictionary<HistoryMetric, List<decimal>>> buckets = new();

        foreach (RequestHistoryItem request in requests)
        {
            DateOnly day = DateOnly.FromDateTime(request.StartedAt.UtcDateTime);
            Dictionary<HistoryMetric, List<decimal>> bucket = GetBucket(buckets, day);
            foreach ((HistoryMetric key, MetricValue metric) in request.Metrics)
            {
                if (metric.Value is decimal value)
                {
                    AddSample(bucket, key, value);
                }
            }

            AddSample(
                bucket,
                HistoryMetric.ErrorRatePercent,
                request.ErrorType == HistoryErrorType.None ? 0 : 100);
        }

        foreach (TechnicalResourceSampleRecord resource in resources)
        {
            DateOnly day = DateOnly.FromDateTime(resource.CapturedAt.UtcDateTime);
            Dictionary<HistoryMetric, List<decimal>> bucket = GetBucket(buckets, day);
            if (resource.CpuPercent.Value is decimal cpu)
            {
                AddSample(bucket, HistoryMetric.CpuPercent, cpu);
            }

            if (resource.MemoryPercent.Value is decimal memory)
            {
                AddSample(bucket, HistoryMetric.MemoryPercent, memory);
            }
        }

        HistoryMetric[] trendMetrics =
        [
            HistoryMetric.InputTokens,
            HistoryMetric.OutputTokens,
            HistoryMetric.TimeToFirstTokenMilliseconds,
            HistoryMetric.PromptTokensPerSecond,
            HistoryMetric.GenerationTokensPerSecond,
            HistoryMetric.ContextUsageTokens,
            HistoryMetric.ModelLoadMilliseconds,
            HistoryMetric.CpuPercent,
            HistoryMetric.MemoryPercent,
            HistoryMetric.ErrorRatePercent,
        ];
        AnalyticsTrendPoint[] trend = buckets
            .OrderBy(item => item.Key)
            .Select(item => new AnalyticsTrendPoint(
                item.Key,
                trendMetrics.ToDictionary(
                    metric => metric,
                    metric => HistoryStatistics.Calculate(
                        item.Value.TryGetValue(metric, out List<decimal>? values) ? values : []))))
            .ToArray();
        return new PeriodAnalytics(
            filter,
            trend,
            new ModelLoadBreakdown(
                requests.Count(item => item.ModelLoadDisposition == ModelLoadDisposition.Cold),
                requests.Count(item => item.ModelLoadDisposition == ModelLoadDisposition.Warm),
                requests.Count(item => item.ModelLoadDisposition == ModelLoadDisposition.Unavailable)));
    }

    public async Task<AnalyticsComparison> CompareAsync(
        HistoryFilter baseline,
        HistoryFilter candidate,
        HistoryMetric metric,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<decimal> baselineSamples = await ReadMetricSamplesAsync(baseline, metric, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<decimal> candidateSamples = await ReadMetricSamplesAsync(candidate, metric, cancellationToken)
            .ConfigureAwait(false);
        return HistoryStatistics.Compare(metric, baselineSamples, candidateSamples);
    }

    public async Task<HistoryRetention> GetRetentionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT retention FROM history_settings WHERE id = 1;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is long value && Enum.IsDefined((HistoryRetention)value)
            ? (HistoryRetention)value
            : throw new InvalidDataException("Persisted history retention is invalid.");
    }

    public async Task SetRetentionAsync(
        HistoryRetention retention,
        CancellationToken cancellationToken = default)
    {
        _ = HistoryPolicies.GetRetentionDuration(retention);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE history_settings SET retention = $retention WHERE id = 1;";
            command.Parameters.AddWithValue("$retention", (int)retention);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (affected != 1)
            {
                throw new InvalidDataException("History retention settings row is missing.");
            }
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task<int> ApplyRetentionAsync(
        HistoryRetention retention,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        TimeSpan? duration = HistoryPolicies.GetRetentionDuration(retention);
        if (duration is null)
        {
            return 0;
        }

        DateTimeOffset cutoff = now.ToUniversalTime() - duration.Value;
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            int deleted = 0;
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "resource_samples",
                "captured_at_utc",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "tool_events",
                "started_at_utc",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "turns",
                "started_at_utc",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "requests",
                "started_at_utc",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "operations",
                "COALESCE(ended_at_utc, started_at_utc)",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            deleted += await DeleteBeforeInBatchesAsync(
                connection,
                "sessions",
                "COALESCE(ended_at_utc, started_at_utc)",
                cutoff,
                cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public async Task<HistoryClearPreview> PreviewClearAsync(
        HistoryClearScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await ReadClearPreviewAsync(connection, null, scope, cancellationToken).ConfigureAwait(false);
    }

    public async Task<HistoryClearPreview> ClearAsync(
        HistoryClearPreview preview,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!confirmed)
        {
            throw new InvalidOperationException("Explicit confirmation is required to clear history.");
        }

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await _writerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            HistoryClearPreview current = await ReadClearPreviewAsync(
                connection,
                transaction,
                preview.Scope,
                cancellationToken).ConfigureAwait(false);
            if (current != preview)
            {
                throw new InvalidOperationException("History changed after preview; review the scope again.");
            }

            foreach (string table in new[]
                     {
                         "resource_samples",
                         "tool_events",
                         "turns",
                         "requests",
                         "operations",
                         "sessions",
                     })
            {
                await DeleteScopeAsync(connection, transaction, table, preview.Scope, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return current;
        }
        finally
        {
            _writerLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _writerLock.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static async Task InsertRequestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProxyObservation observation,
        CancellationToken cancellationToken)
    {
        RequestCorrelation? correlation = observation.Correlation;
        if (correlation is not null)
        {
            await UpsertSessionAsync(
                connection,
                transaction,
                new TechnicalSessionRecord(
                    correlation.SessionId,
                    observation.StartedAt,
                    observation.StartedAt + observation.Duration,
                    observation.Client,
                    observation.BackendTelemetry.Backend,
                    observation.BackendTelemetry.Model),
                cancellationToken).ConfigureAwait(false);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO requests(
                request_id, session_id, operation_id, started_at_utc, http_status_code,
                outcome, error_type, client, backend, model,
                correlation_turn_id, correlation_turn_sequence, model_load_disposition)
            VALUES(
                $request_id, $session_id, NULL, $started_at, $http_status,
                $outcome, $error_type, $client, $backend, $model,
                $correlation_turn_id, $correlation_turn_sequence, $model_load_disposition);
            """;
        command.Parameters.AddWithValue("$request_id", observation.RequestId.ToString("N"));
        command.Parameters.AddWithValue("$session_id", DbValue(correlation?.SessionId.ToString("N")));
        command.Parameters.AddWithValue("$started_at", ToDbTime(observation.StartedAt));
        command.Parameters.AddWithValue("$http_status", DbValue(observation.HttpStatusCode));
        command.Parameters.AddWithValue("$outcome", (int)observation.Outcome);
        command.Parameters.AddWithValue("$error_type", (int)MapErrorType(observation.Outcome));
        command.Parameters.AddWithValue("$client", (int)observation.Client);
        command.Parameters.AddWithValue("$backend", (int)observation.BackendTelemetry.Backend);
        command.Parameters.AddWithValue("$model", DbValue(observation.BackendTelemetry.Model?.Value));
        command.Parameters.AddWithValue("$correlation_turn_id", DbValue(correlation?.TurnId.ToString("N")));
        command.Parameters.AddWithValue("$correlation_turn_sequence", DbValue(correlation?.TurnSequence));
        command.Parameters.AddWithValue(
            "$model_load_disposition",
            (int)observation.BackendTelemetry.ModelLoadDisposition);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach ((HistoryMetric key, MetricValue value) in GetObservationMetrics(observation))
        {
            await InsertRequestMetricAsync(
                connection,
                transaction,
                observation.RequestId,
                key,
                value,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<HistoryMetric, MetricValue> GetObservationMetrics(
        ProxyObservation observation)
    {
        BackendResponseTelemetry telemetry = observation.BackendTelemetry;
        MetricValue duration = MetricValue.Calculated(
            (decimal)observation.Duration.TotalMilliseconds,
            MetricUnit.Milliseconds,
            MetricSource.Inspector,
            "monotonic-clock-v1",
            "monotonic-request-duration-v1");
        return new Dictionary<HistoryMetric, MetricValue>
        {
            [HistoryMetric.InputTokens] = telemetry.PromptTokens,
            [HistoryMetric.OutputTokens] = telemetry.CompletionTokens,
            [HistoryMetric.TotalTokens] = telemetry.TotalTokens,
            [HistoryMetric.CachedTokens] = telemetry.CachedPromptTokens,
            [HistoryMetric.ReasoningTokens] = telemetry.ReasoningTokens,
            [HistoryMetric.ContextUsageTokens] = telemetry.ContextUsageTokens,
            [HistoryMetric.ContextLimitTokens] = telemetry.ContextLimitTokens,
            [HistoryMetric.ContextHistoryTokens] = telemetry.ContextHistoryTokens,
            [HistoryMetric.ContextToolTokens] = telemetry.ContextToolTokens,
            [HistoryMetric.PromptTokensPerSecond] = telemetry.PromptTokensPerSecond,
            [HistoryMetric.GenerationTokensPerSecond] = telemetry.CompletionTokensPerSecond,
            [HistoryMetric.TimeToFirstTokenMilliseconds] = observation.TimeToFirstToken,
            [HistoryMetric.ModelLoadMilliseconds] = telemetry.ModelLoadTime,
            [HistoryMetric.QueueMilliseconds] = telemetry.QueueTime,
            [HistoryMetric.TotalDurationMilliseconds] = duration,
        };
    }

    private static async Task InsertRequestMetricAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid requestId,
        HistoryMetric key,
        MetricValue metric,
        CancellationToken cancellationToken)
    {
        ValidateMetricMetadata(metric);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO request_metrics(
                request_id, metric_key, value, unit, quality, source, source_version, derivation_version)
            VALUES(
                $request_id, $metric_key, $value, $unit, $quality, $source, $source_version, $derivation_version);
            """;
        command.Parameters.AddWithValue("$request_id", requestId.ToString("N"));
        command.Parameters.AddWithValue("$metric_key", MetricKey(key));
        command.Parameters.AddWithValue("$value", DbValue(metric.Value));
        command.Parameters.AddWithValue("$unit", (int)metric.Unit);
        command.Parameters.AddWithValue("$quality", (int)metric.Quality);
        command.Parameters.AddWithValue("$source", (int)metric.Source);
        command.Parameters.AddWithValue("$source_version", metric.SourceVersion);
        command.Parameters.AddWithValue("$derivation_version", DbValue(metric.DerivationVersion));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TechnicalSessionRecord session,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sessions(session_id, started_at_utc, ended_at_utc, client, backend, model)
            VALUES($id, $started, $ended, $client, $backend, $model)
            ON CONFLICT(session_id) DO UPDATE SET
                started_at_utc = MIN(sessions.started_at_utc, excluded.started_at_utc),
                ended_at_utc = CASE
                    WHEN sessions.ended_at_utc IS NULL THEN excluded.ended_at_utc
                    WHEN excluded.ended_at_utc IS NULL THEN sessions.ended_at_utc
                    ELSE MAX(sessions.ended_at_utc, excluded.ended_at_utc)
                END,
                client = excluded.client,
                backend = excluded.backend,
                model = excluded.model;
            """;
        command.Parameters.AddWithValue("$id", session.SessionId.ToString("N"));
        command.Parameters.AddWithValue("$started", ToDbTime(session.StartedAt));
        command.Parameters.AddWithValue("$ended", DbValue(session.EndedAt is null ? null : ToDbTime(session.EndedAt.Value)));
        command.Parameters.AddWithValue("$client", (int)session.Client);
        command.Parameters.AddWithValue("$backend", (int)session.Backend);
        command.Parameters.AddWithValue("$model", DbValue(session.Model?.Value));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task UpsertOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TechnicalOperationRecord operation,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO operations(
                operation_id, session_id, started_at_utc, ended_at_utc, client, backend, model, status, error_type)
            VALUES($id, $session, $started, $ended, $client, $backend, $model, $status, $error)
            ON CONFLICT(operation_id) DO UPDATE SET
                ended_at_utc = excluded.ended_at_utc,
                status = excluded.status,
                error_type = excluded.error_type;
            """;
        command.Parameters.AddWithValue("$id", operation.OperationId.ToString("N"));
        command.Parameters.AddWithValue("$session", DbValue(operation.SessionId?.ToString("N")));
        command.Parameters.AddWithValue("$started", ToDbTime(operation.StartedAt));
        command.Parameters.AddWithValue("$ended", DbValue(operation.EndedAt is null ? null : ToDbTime(operation.EndedAt.Value)));
        command.Parameters.AddWithValue("$client", (int)operation.Client);
        command.Parameters.AddWithValue("$backend", (int)operation.Backend);
        command.Parameters.AddWithValue("$model", DbValue(operation.Model?.Value));
        command.Parameters.AddWithValue("$status", (int)operation.Status);
        command.Parameters.AddWithValue("$error", (int)operation.ErrorType);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTurnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TechnicalTurnRecord turn,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO turns(
                turn_id, operation_id, sequence, request_id, started_at_utc, duration_ms, outcome, error_type,
                available_tool_count, available_tool_count_quality, available_tool_count_source,
                available_tool_count_source_version, available_tool_count_derivation_version,
                invoked_tool_count, invoked_tool_count_quality, invoked_tool_count_source,
                invoked_tool_count_source_version, invoked_tool_count_derivation_version)
            VALUES(
                $id, $operation, $sequence, $request, $started, $duration, $outcome, $error,
                $available_value, $available_quality, $available_source, $available_source_version,
                $available_derivation_version,
                $invoked_value, $invoked_quality, $invoked_source, $invoked_source_version,
                $invoked_derivation_version)
            ON CONFLICT(operation_id, sequence) DO UPDATE SET
                request_id = excluded.request_id,
                started_at_utc = excluded.started_at_utc,
                duration_ms = excluded.duration_ms,
                outcome = excluded.outcome,
                error_type = excluded.error_type,
                available_tool_count = excluded.available_tool_count,
                available_tool_count_quality = excluded.available_tool_count_quality,
                available_tool_count_source = excluded.available_tool_count_source,
                available_tool_count_source_version = excluded.available_tool_count_source_version,
                available_tool_count_derivation_version = excluded.available_tool_count_derivation_version,
                invoked_tool_count = excluded.invoked_tool_count,
                invoked_tool_count_quality = excluded.invoked_tool_count_quality,
                invoked_tool_count_source = excluded.invoked_tool_count_source,
                invoked_tool_count_source_version = excluded.invoked_tool_count_source_version,
                invoked_tool_count_derivation_version = excluded.invoked_tool_count_derivation_version;
            """;
        command.Parameters.AddWithValue("$id", turn.TurnId.ToString("N"));
        command.Parameters.AddWithValue("$operation", turn.OperationId.ToString("N"));
        command.Parameters.AddWithValue("$sequence", turn.Sequence);
        command.Parameters.AddWithValue("$request", DbValue(turn.RequestId?.ToString("N")));
        command.Parameters.AddWithValue("$started", ToDbTime(turn.StartedAt));
        command.Parameters.AddWithValue("$duration", turn.Duration.TotalMilliseconds);
        command.Parameters.AddWithValue("$outcome", (int)turn.Outcome);
        command.Parameters.AddWithValue("$error", (int)turn.ErrorType);
        AddCountMetricParameters(command, "available", turn.AvailableToolCount);
        AddCountMetricParameters(command, "invoked", turn.InvokedToolCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task LinkRequestAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid requestId,
        Guid? sessionId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE requests
            SET session_id = $session, operation_id = $operation
            WHERE request_id = $request;
            """;
        command.Parameters.AddWithValue("$session", DbValue(sessionId?.ToString("N")));
        command.Parameters.AddWithValue("$operation", operationId.ToString("N"));
        command.Parameters.AddWithValue("$request", requestId.ToString("N"));
        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            throw new InvalidOperationException("A turn can only reference an existing technical request record.");
        }
    }

    private static async Task InsertToolEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TechnicalToolEventRecord tool,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tool_events(
                tool_event_id, operation_id, turn_sequence, sequence, tool_name,
                started_at_utc, duration_ms, duration_quality, duration_source,
                duration_source_version, duration_derivation_version, status, error_type)
            VALUES(
                $id, $operation, $turn_sequence, $sequence, $name, $started, $duration,
                $duration_quality, $duration_source, $duration_source_version,
                $duration_derivation_version, $status, $error)
            ON CONFLICT(operation_id, turn_sequence, sequence) DO UPDATE SET
                tool_name = excluded.tool_name,
                started_at_utc = excluded.started_at_utc,
                duration_ms = excluded.duration_ms,
                duration_quality = excluded.duration_quality,
                duration_source = excluded.duration_source,
                duration_source_version = excluded.duration_source_version,
                duration_derivation_version = excluded.duration_derivation_version,
                status = excluded.status,
                error_type = excluded.error_type;
            """;
        command.Parameters.AddWithValue("$id", tool.ToolEventId.ToString("N"));
        command.Parameters.AddWithValue("$operation", tool.OperationId.ToString("N"));
        command.Parameters.AddWithValue("$turn_sequence", tool.TurnSequence);
        command.Parameters.AddWithValue("$sequence", tool.Sequence);
        command.Parameters.AddWithValue("$name", tool.ToolName.Value);
        command.Parameters.AddWithValue("$started", ToDbTime(tool.StartedAt));
        command.Parameters.AddWithValue("$duration", tool.Duration.TotalMilliseconds);
        ValidateMetricMetadata(tool.DurationMetric);
        command.Parameters.AddWithValue("$duration_quality", (int)tool.DurationMetric.Quality);
        command.Parameters.AddWithValue("$duration_source", (int)tool.DurationMetric.Source);
        command.Parameters.AddWithValue("$duration_source_version", tool.DurationMetric.SourceVersion);
        command.Parameters.AddWithValue("$duration_derivation_version", DbValue(tool.DurationMetric.DerivationVersion));
        command.Parameters.AddWithValue("$status", (int)tool.Status);
        command.Parameters.AddWithValue("$error", (int)tool.ErrorType);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertResourceSampleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TechnicalResourceSampleRecord sample,
        CancellationToken cancellationToken)
    {
        if (sample.CpuPercent.Unit != MetricUnit.Percent || sample.MemoryPercent.Unit != MetricUnit.Percent)
        {
            throw new ArgumentException("Resource load metrics must use percent units.", nameof(sample));
        }

        foreach ((_, MetricValue metric) in ResourceMetrics(sample))
        {
            ValidateMetricMetadata(metric);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO resource_samples(
                sample_id, operation_id, request_id, captured_at_utc,
                stage, stage_evidence, stage_source_version,
                process_id, process_started_at_utc, process_image_name, process_association_source_version,
                gpu_device_id, dropped_sample_count,
                cpu_percent, cpu_quality, cpu_source, cpu_source_version, cpu_derivation_version,
                memory_percent, memory_quality, memory_source, memory_source_version, memory_derivation_version)
            VALUES(
                $id, $operation, $request, $captured,
                $stage, $stage_evidence, $stage_source_version,
                $process_id, $process_started, $process_image, $process_source_version,
                $gpu_device_id, $dropped_sample_count,
                $cpu, $cpu_quality, $cpu_source, $cpu_source_version, $cpu_derivation_version,
                $memory, $memory_quality, $memory_source, $memory_source_version, $memory_derivation_version)
            ON CONFLICT(sample_id) DO UPDATE SET
                operation_id = excluded.operation_id,
                request_id = excluded.request_id,
                captured_at_utc = excluded.captured_at_utc,
                stage = excluded.stage,
                stage_evidence = excluded.stage_evidence,
                stage_source_version = excluded.stage_source_version,
                process_id = excluded.process_id,
                process_started_at_utc = excluded.process_started_at_utc,
                process_image_name = excluded.process_image_name,
                process_association_source_version = excluded.process_association_source_version,
                gpu_device_id = excluded.gpu_device_id,
                dropped_sample_count = excluded.dropped_sample_count,
                cpu_percent = excluded.cpu_percent,
                cpu_quality = excluded.cpu_quality,
                cpu_source = excluded.cpu_source,
                cpu_source_version = excluded.cpu_source_version,
                cpu_derivation_version = excluded.cpu_derivation_version,
                memory_percent = excluded.memory_percent,
                memory_quality = excluded.memory_quality,
                memory_source = excluded.memory_source,
                memory_source_version = excluded.memory_source_version,
                memory_derivation_version = excluded.memory_derivation_version;
            """;
        command.Parameters.AddWithValue("$id", sample.SampleId.ToString("N"));
        command.Parameters.AddWithValue("$operation", DbValue(sample.OperationId?.ToString("N")));
        command.Parameters.AddWithValue("$request", DbValue(sample.RequestId?.ToString("N")));
        command.Parameters.AddWithValue("$captured", ToDbTime(sample.CapturedAt));
        command.Parameters.AddWithValue("$stage", DbValue(sample.Stage is null ? null : (int)sample.Stage.Stage));
        command.Parameters.AddWithValue(
            "$stage_evidence",
            DbValue(sample.Stage is null ? null : (int)sample.Stage.Evidence));
        command.Parameters.AddWithValue("$stage_source_version", DbValue(sample.Stage?.SourceVersion));
        command.Parameters.AddWithValue("$process_id", DbValue(sample.RelatedProcess?.ProcessId));
        command.Parameters.AddWithValue(
            "$process_started",
            DbValue(sample.RelatedProcess is null ? null : ToDbTime(sample.RelatedProcess.ProcessStartedAt)));
        command.Parameters.AddWithValue("$process_image", DbValue(sample.RelatedProcess?.ImageName.Value));
        command.Parameters.AddWithValue("$process_source_version", DbValue(sample.RelatedProcess?.SourceVersion));
        command.Parameters.AddWithValue("$gpu_device_id", DbValue(sample.GpuDeviceId?.Value));
        command.Parameters.AddWithValue("$dropped_sample_count", sample.DroppedSampleCount);
        command.Parameters.AddWithValue("$cpu", DbValue(sample.CpuPercent.Value));
        command.Parameters.AddWithValue("$cpu_quality", (int)sample.CpuPercent.Quality);
        command.Parameters.AddWithValue("$cpu_source", (int)sample.CpuPercent.Source);
        command.Parameters.AddWithValue("$cpu_source_version", sample.CpuPercent.SourceVersion);
        command.Parameters.AddWithValue("$cpu_derivation_version", DbValue(sample.CpuPercent.DerivationVersion));
        command.Parameters.AddWithValue("$memory", DbValue(sample.MemoryPercent.Value));
        command.Parameters.AddWithValue("$memory_quality", (int)sample.MemoryPercent.Quality);
        command.Parameters.AddWithValue("$memory_source", (int)sample.MemoryPercent.Source);
        command.Parameters.AddWithValue("$memory_source_version", sample.MemoryPercent.SourceVersion);
        command.Parameters.AddWithValue("$memory_derivation_version", DbValue(sample.MemoryPercent.DerivationVersion));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach ((string key, MetricValue metric) in ResourceMetrics(sample))
        {
            await InsertResourceMetricAsync(
                connection,
                transaction,
                sample.SampleId,
                key,
                metric,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertResourceMetricAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sampleId,
        string key,
        MetricValue metric,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO resource_sample_metrics(
                sample_id, metric_key, value, unit, quality, source, source_version, derivation_version)
            VALUES($sample, $key, $value, $unit, $quality, $source, $source_version, $derivation)
            ON CONFLICT(sample_id, metric_key) DO UPDATE SET
                value = excluded.value,
                unit = excluded.unit,
                quality = excluded.quality,
                source = excluded.source,
                source_version = excluded.source_version,
                derivation_version = excluded.derivation_version;
            """;
        command.Parameters.AddWithValue("$sample", sampleId.ToString("N"));
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", DbValue(metric.Value));
        command.Parameters.AddWithValue("$unit", (int)metric.Unit);
        command.Parameters.AddWithValue("$quality", (int)metric.Quality);
        command.Parameters.AddWithValue("$source", (int)metric.Source);
        command.Parameters.AddWithValue("$source_version", metric.SourceVersion);
        command.Parameters.AddWithValue("$derivation", DbValue(metric.DerivationVersion));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsCoreAsync(
        HistoryFilter filter,
        int? limit,
        CancellationToken cancellationToken)
    {
        ValidateFilter(filter);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        List<string> predicates = AddFilterParameters(command, filter, "r");
        command.CommandText = $"""
            SELECT r.request_id, r.session_id, r.operation_id, r.started_at_utc,
                   r.http_status_code, r.outcome, r.error_type, r.client, r.backend, r.model,
                   r.model_load_disposition, r.correlation_turn_id, r.correlation_turn_sequence
            FROM requests r
            {(predicates.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", predicates))}
            ORDER BY r.started_at_utc DESC, r.request_id DESC
            {(limit is null ? string.Empty : "LIMIT $limit")};
            """;
        if (limit is not null)
        {
            command.Parameters.AddWithValue("$limit", limit.Value);
        }

        List<RequestRow> rows = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rows.Add(ReadRequestRow(reader));
            }
        }

        Dictionary<Guid, Dictionary<HistoryMetric, MetricValue>> metrics = await ReadRequestMetricsAsync(
            connection,
            rows.Select(item => item.RequestId).ToArray(),
            cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new RequestHistoryItem(
            row.RequestId,
            row.SessionId,
            row.OperationId,
            row.StartedAt,
            row.HttpStatusCode,
            row.Outcome,
            row.ErrorType,
            row.Client,
            row.Backend,
            row.Model,
            metrics.TryGetValue(row.RequestId, out Dictionary<HistoryMetric, MetricValue>? values)
                ? values
                : new Dictionary<HistoryMetric, MetricValue>(),
            row.ModelLoadDisposition,
            row.CorrelatedTurnId,
            row.CorrelatedTurnSequence)).ToArray();
    }

    private static async Task<Dictionary<Guid, Dictionary<HistoryMetric, MetricValue>>> ReadRequestMetricsAsync(
        SqliteConnection connection,
        Guid[] requestIds,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, Dictionary<HistoryMetric, MetricValue>> result = [];
        if (requestIds.Length == 0)
        {
            return result;
        }

        await using SqliteCommand command = connection.CreateCommand();
        List<string> parameters = [];
        for (int index = 0; index < requestIds.Length; index++)
        {
            string name = $"$id{index}";
            parameters.Add(name);
            command.Parameters.AddWithValue(name, requestIds[index].ToString("N"));
        }

        command.CommandText = $"""
            SELECT request_id, metric_key, value, unit, quality, source, source_version, derivation_version
            FROM request_metrics
            WHERE request_id IN ({string.Join(", ", parameters)});
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            Guid requestId = Guid.ParseExact(reader.GetString(0), "N");
            if (!result.TryGetValue(requestId, out Dictionary<HistoryMetric, MetricValue>? requestMetrics))
            {
                requestMetrics = [];
                result.Add(requestId, requestMetrics);
            }

            HistoryMetric key = ParseMetricKey(reader.GetString(1));
            decimal? value = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
            string? derivation = reader.IsDBNull(7) ? null : reader.GetString(7);
            requestMetrics[key] = new MetricValue(
                value,
                (MetricUnit)reader.GetInt32(3),
                (MetricQuality)reader.GetInt32(4),
                (MetricSource)reader.GetInt32(5),
                reader.GetString(6),
                derivation);
        }

        return result;
    }

    private static List<string> AddFilterParameters(
        SqliteCommand command,
        HistoryFilter filter,
        string alias)
    {
        List<string> predicates = [];
        if (filter.From is not null)
        {
            predicates.Add($"{alias}.started_at_utc >= $from");
            command.Parameters.AddWithValue("$from", ToDbTime(filter.From.Value));
        }

        if (filter.To is not null)
        {
            predicates.Add($"{alias}.started_at_utc <= $to");
            command.Parameters.AddWithValue("$to", ToDbTime(filter.To.Value));
        }

        AddEnumPredicate(command, predicates, $"{alias}.client", "$client", filter.Client);
        AddEnumPredicate(command, predicates, $"{alias}.backend", "$backend", filter.Backend);
        AddEnumPredicate(command, predicates, $"{alias}.outcome", "$status", filter.Status);
        AddEnumPredicate(command, predicates, $"{alias}.error_type", "$error", filter.ErrorType);
        if (filter.Model is not null)
        {
            predicates.Add($"{alias}.model = $model");
            command.Parameters.AddWithValue("$model", filter.Model.Value);
        }

        if (filter.SessionId is not null)
        {
            predicates.Add($"{alias}.session_id = $session");
            command.Parameters.AddWithValue("$session", filter.SessionId.Value.ToString("N"));
        }

        return predicates;
    }

    private static void AddEnumPredicate<T>(
        SqliteCommand command,
        List<string> predicates,
        string column,
        string parameter,
        T? value)
        where T : struct, Enum
    {
        if (value is not null)
        {
            predicates.Add($"{column} = {parameter}");
            command.Parameters.AddWithValue(parameter, Convert.ToInt32(value.Value, CultureInfo.InvariantCulture));
        }
    }

    private static RequestRow ReadRequestRow(SqliteDataReader reader) => new(
        Guid.ParseExact(reader.GetString(0), "N"),
        reader.IsDBNull(1) ? null : Guid.ParseExact(reader.GetString(1), "N"),
        reader.IsDBNull(2) ? null : Guid.ParseExact(reader.GetString(2), "N"),
        ParseDbTime(reader.GetString(3)),
        reader.IsDBNull(4) ? null : reader.GetInt32(4),
        (ProxyOutcome)reader.GetInt32(5),
        (HistoryErrorType)reader.GetInt32(6),
        (ClientKind)reader.GetInt32(7),
        (BackendKind)reader.GetInt32(8),
        reader.IsDBNull(9) ? null : ReadIdentifier(reader.GetString(9)),
        ReadModelLoadDisposition(reader.GetInt32(10)),
        reader.IsDBNull(11) ? null : Guid.ParseExact(reader.GetString(11), "N"),
        reader.IsDBNull(12) ? null : reader.GetInt32(12));

    private static async Task<TechnicalOperationRecord?> ReadOperationAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT operation_id, session_id, started_at_utc, ended_at_utc,
                   client, backend, model, status, error_type
            FROM operations WHERE operation_id = $id;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new TechnicalOperationRecord(
            Guid.ParseExact(reader.GetString(0), "N"),
            reader.IsDBNull(1) ? null : Guid.ParseExact(reader.GetString(1), "N"),
            ParseDbTime(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseDbTime(reader.GetString(3)),
            (ClientKind)reader.GetInt32(4),
            (BackendKind)reader.GetInt32(5),
            reader.IsDBNull(6) ? null : ReadIdentifier(reader.GetString(6)),
            (TechnicalOperationStatus)reader.GetInt32(7),
            (HistoryErrorType)reader.GetInt32(8));
    }

    private static async Task<IReadOnlyList<TechnicalTurnRecord>> ReadTurnsAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT turn_id, operation_id, sequence, request_id, started_at_utc,
                   duration_ms, outcome, error_type,
                   available_tool_count, available_tool_count_quality, available_tool_count_source,
                   available_tool_count_source_version, available_tool_count_derivation_version,
                   invoked_tool_count, invoked_tool_count_quality, invoked_tool_count_source,
                   invoked_tool_count_source_version, invoked_tool_count_derivation_version
            FROM turns WHERE operation_id = $id ORDER BY sequence, started_at_utc;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        List<TechnicalTurnRecord> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TechnicalTurnRecord(
                Guid.ParseExact(reader.GetString(0), "N"),
                Guid.ParseExact(reader.GetString(1), "N"),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : Guid.ParseExact(reader.GetString(3), "N"),
                ParseDbTime(reader.GetString(4)),
                TimeSpan.FromMilliseconds(reader.GetDouble(5)),
                (ProxyOutcome)reader.GetInt32(6),
                (HistoryErrorType)reader.GetInt32(7))
            {
                AvailableToolCount = CreateStoredMetric(
                    reader, MetricUnit.Count, 8, 9, 10, 11, 12),
                InvokedToolCount = CreateStoredMetric(
                    reader, MetricUnit.Count, 13, 14, 15, 16, 17),
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<TechnicalToolEventRecord>> ReadToolsAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT tool_event_id, operation_id, turn_sequence, sequence, tool_name,
                   started_at_utc, duration_ms, duration_quality, duration_source,
                   duration_source_version, duration_derivation_version, status, error_type
            FROM tool_events
            WHERE operation_id = $id
            ORDER BY turn_sequence, sequence, started_at_utc;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        List<TechnicalToolEventRecord> result = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TechnicalToolEventRecord(
                Guid.ParseExact(reader.GetString(0), "N"),
                Guid.ParseExact(reader.GetString(1), "N"),
                reader.GetInt32(2),
                reader.GetInt32(3),
                ReadIdentifier(reader.GetString(4)),
                ParseDbTime(reader.GetString(5)),
                TimeSpan.FromMilliseconds(reader.GetDouble(6)),
                (TechnicalToolStatus)reader.GetInt32(11),
                (HistoryErrorType)reader.GetInt32(12))
            {
                DurationMetric = CreateStoredMetric(
                    reader, MetricUnit.Milliseconds, 6, 7, 8, 9, 10),
            });
        }

        return result;
    }

    private static async Task<IReadOnlyList<TechnicalResourceSampleRecord>> ReadResourcesAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sample_id, operation_id, captured_at_utc,
                   cpu_percent, cpu_quality, cpu_source, cpu_source_version, cpu_derivation_version,
                   memory_percent, memory_quality, memory_source, memory_source_version, memory_derivation_version,
                   request_id, stage, stage_evidence, stage_source_version,
                   process_id, process_started_at_utc, process_image_name, process_association_source_version,
                   gpu_device_id, dropped_sample_count
            FROM resource_samples
            WHERE operation_id = $id
            ORDER BY captured_at_utc, sample_id;
            """;
        command.Parameters.AddWithValue("$id", operationId.ToString("N"));
        return await ReadResourcesAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TechnicalResourceSampleRecord>> QueryResourcesAsync(
        HistoryFilter filter,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        List<string> predicates = [];
        if (filter.From is not null)
        {
            predicates.Add("s.captured_at_utc >= $from");
            command.Parameters.AddWithValue("$from", ToDbTime(filter.From.Value));
        }

        if (filter.To is not null)
        {
            predicates.Add("s.captured_at_utc <= $to");
            command.Parameters.AddWithValue("$to", ToDbTime(filter.To.Value));
        }

        AddEnumPredicate(command, predicates, "COALESCE(o.client, r.client)", "$client", filter.Client);
        AddEnumPredicate(command, predicates, "COALESCE(o.backend, r.backend)", "$backend", filter.Backend);
        if (filter.Model is not null)
        {
            predicates.Add("COALESCE(o.model, r.model) = $model");
            command.Parameters.AddWithValue("$model", filter.Model.Value);
        }

        if (filter.SessionId is not null)
        {
            predicates.Add("COALESCE(o.session_id, r.session_id) = $session");
            command.Parameters.AddWithValue("$session", filter.SessionId.Value.ToString("N"));
        }

        if (filter.Status is not null)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM requests filtered_request
                    WHERE (filtered_request.request_id = s.request_id
                           OR (s.request_id IS NULL AND filtered_request.operation_id = s.operation_id))
                      AND filtered_request.outcome = $status
                )
                """);
            command.Parameters.AddWithValue("$status", (int)filter.Status.Value);
        }

        if (filter.ErrorType is not null)
        {
            predicates.Add("""
                EXISTS (
                    SELECT 1
                    FROM requests filtered_request
                    WHERE (filtered_request.request_id = s.request_id
                           OR (s.request_id IS NULL AND filtered_request.operation_id = s.operation_id))
                      AND filtered_request.error_type = $error
                )
                """);
            command.Parameters.AddWithValue("$error", (int)filter.ErrorType.Value);
        }

        command.CommandText = $"""
            SELECT s.sample_id, s.operation_id, s.captured_at_utc,
                   s.cpu_percent, s.cpu_quality, s.cpu_source, s.cpu_source_version, s.cpu_derivation_version,
                   s.memory_percent, s.memory_quality, s.memory_source, s.memory_source_version,
                   s.memory_derivation_version,
                   s.request_id, s.stage, s.stage_evidence, s.stage_source_version,
                   s.process_id, s.process_started_at_utc, s.process_image_name,
                   s.process_association_source_version, s.gpu_device_id, s.dropped_sample_count
            FROM resource_samples s
            LEFT JOIN operations o ON o.operation_id = s.operation_id
            LEFT JOIN requests r ON r.request_id = s.request_id
            {(predicates.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", predicates))}
            ORDER BY s.captured_at_utc, s.sample_id;
            """;
        return await ReadResourcesAsync(connection, command, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TechnicalResourceSampleRecord>> ReadResourcesAsync(
        SqliteConnection connection,
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        List<TechnicalResourceSampleRecord> result = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                RequestStageValue? stage = reader.IsDBNull(14)
                    ? null
                    : new RequestStageValue(
                        (RequestStage)reader.GetInt32(14),
                        (RequestStageEvidence)reader.GetInt32(15),
                        reader.GetString(16));
                TechnicalProcessAssociation? process = reader.IsDBNull(17)
                    ? null
                    : new TechnicalProcessAssociation(
                        reader.GetInt32(17),
                        ParseDbTime(reader.GetString(18)),
                        ReadIdentifier(reader.GetString(19)),
                        reader.GetString(20));
                result.Add(new TechnicalResourceSampleRecord(
                    Guid.ParseExact(reader.GetString(0), "N"),
                    reader.IsDBNull(1) ? null : Guid.ParseExact(reader.GetString(1), "N"),
                    ParseDbTime(reader.GetString(2)),
                    CreateStoredPercentMetric(reader, 3, 4, 5, 6, 7),
                    CreateStoredPercentMetric(reader, 8, 9, 10, 11, 12))
                {
                    RequestId = reader.IsDBNull(13) ? null : Guid.ParseExact(reader.GetString(13), "N"),
                    Stage = stage,
                    RelatedProcess = process,
                    GpuDeviceId = reader.IsDBNull(21) ? null : ReadIdentifier(reader.GetString(21)),
                    DroppedSampleCount = reader.GetInt32(22),
                });
            }
        }

        return await HydrateResourceMetricsAsync(connection, result, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<TechnicalResourceSampleRecord>> HydrateResourceMetricsAsync(
        SqliteConnection connection,
        List<TechnicalResourceSampleRecord> samples,
        CancellationToken cancellationToken)
    {
        const int batchSize = 400;
        Dictionary<Guid, Dictionary<string, MetricValue>> metricsBySample = [];
        for (int offset = 0; offset < samples.Count; offset += batchSize)
        {
            TechnicalResourceSampleRecord[] batch = samples.Skip(offset).Take(batchSize).ToArray();
            await using SqliteCommand command = connection.CreateCommand();
            string[] parameterNames = new string[batch.Length];
            for (int index = 0; index < batch.Length; index++)
            {
                parameterNames[index] = $"$sample_{index}";
                command.Parameters.AddWithValue(parameterNames[index], batch[index].SampleId.ToString("N"));
            }

            command.CommandText = $"""
                SELECT sample_id, metric_key, value, unit, quality, source, source_version, derivation_version
                FROM resource_sample_metrics
                WHERE sample_id IN ({string.Join(", ", parameterNames)});
                """;
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                Guid sampleId = Guid.ParseExact(reader.GetString(0), "N");
                if (!metricsBySample.TryGetValue(sampleId, out Dictionary<string, MetricValue>? metrics))
                {
                    metrics = new Dictionary<string, MetricValue>(StringComparer.Ordinal);
                    metricsBySample.Add(sampleId, metrics);
                }

                metrics[reader.GetString(1)] = CreateStoredMetric(
                    reader,
                    (MetricUnit)reader.GetInt32(3),
                    2,
                    4,
                    5,
                    6,
                    7);
            }
        }

        return samples.Select(sample => ApplyResourceMetrics(
            sample,
            metricsBySample.TryGetValue(sample.SampleId, out Dictionary<string, MetricValue>? metrics)
                ? metrics
                : [])).ToArray();
    }

    private static TechnicalResourceSampleRecord ApplyResourceMetrics(
        TechnicalResourceSampleRecord sample,
        Dictionary<string, MetricValue> metrics) =>
        sample with
        {
            CpuPercent = GetResourceMetric(metrics, ResourceMetricSystemCpu, sample.CpuPercent),
            MemoryPercent = GetResourceMetric(metrics, ResourceMetricSystemMemory, sample.MemoryPercent),
            MemoryUsedBytes = GetResourceMetric(metrics, ResourceMetricSystemMemoryUsed, sample.MemoryUsedBytes),
            ProcessCpuPercent = GetResourceMetric(metrics, ResourceMetricProcessCpu, sample.ProcessCpuPercent),
            ProcessMemoryBytes = GetResourceMetric(metrics, ResourceMetricProcessMemory, sample.ProcessMemoryBytes),
            DiskReadBytes = GetResourceMetric(metrics, ResourceMetricDiskRead, sample.DiskReadBytes),
            DiskWriteBytes = GetResourceMetric(metrics, ResourceMetricDiskWrite, sample.DiskWriteBytes),
            ClientToBackendBytes = GetResourceMetric(metrics, ResourceMetricClientToBackend, sample.ClientToBackendBytes),
            BackendToClientBytes = GetResourceMetric(metrics, ResourceMetricBackendToClient, sample.BackendToClientBytes),
            GpuUtilizationPercent = GetResourceMetric(metrics, ResourceMetricGpuUtilization, sample.GpuUtilizationPercent),
            GpuVramUsedBytes = GetResourceMetric(metrics, ResourceMetricGpuVramUsed, sample.GpuVramUsedBytes),
            GpuVramTotalBytes = GetResourceMetric(metrics, ResourceMetricGpuVramTotal, sample.GpuVramTotalBytes),
            GpuTemperatureCelsius = GetResourceMetric(metrics, ResourceMetricGpuTemperature, sample.GpuTemperatureCelsius),
            GpuPowerWatts = GetResourceMetric(metrics, ResourceMetricGpuPower, sample.GpuPowerWatts),
        };

    private async Task<IReadOnlyList<decimal>> ReadMetricSamplesAsync(
        HistoryFilter filter,
        HistoryMetric metric,
        CancellationToken cancellationToken)
    {
        if (metric is HistoryMetric.CpuPercent or HistoryMetric.MemoryPercent)
        {
            IReadOnlyList<TechnicalResourceSampleRecord> resources = await QueryResourcesAsync(filter, cancellationToken)
                .ConfigureAwait(false);
            return resources
                .Select(item => metric == HistoryMetric.CpuPercent ? item.CpuPercent.Value : item.MemoryPercent.Value)
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .ToArray();
        }

        IReadOnlyList<RequestHistoryItem> requests = await QueryRequestsCoreAsync(filter, null, cancellationToken)
            .ConfigureAwait(false);
        if (metric == HistoryMetric.ErrorRatePercent)
        {
            return requests.Select(item => item.ErrorType == HistoryErrorType.None ? 0m : 100m).ToArray();
        }

        return requests
            .Select(item => item.Metrics.TryGetValue(metric, out MetricValue? value) ? value.Value : null)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
    }

    private static async Task<int> DeleteBeforeInBatchesAsync(
        SqliteConnection connection,
        string table,
        string timestampExpression,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (true)
        {
            await using SqliteTransaction transaction = (SqliteTransaction)await connection
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                DELETE FROM {table}
                WHERE rowid IN (
                    SELECT rowid FROM {table}
                    WHERE {timestampExpression} < $cutoff
                    ORDER BY {timestampExpression}, rowid
                    LIMIT $batch_size
                );
                """;
            command.Parameters.AddWithValue("$cutoff", ToDbTime(cutoff));
            command.Parameters.AddWithValue("$batch_size", HistoryPolicies.RetentionDeleteBatchSize);
            int count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            total += count;
            if (count < HistoryPolicies.RetentionDeleteBatchSize)
            {
                return total;
            }
        }
    }

    private static async Task<HistoryClearPreview> ReadClearPreviewAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        HistoryClearScope scope,
        CancellationToken cancellationToken)
    {
        int requests = await CountScopeAsync(connection, transaction, "requests", scope, cancellationToken)
            .ConfigureAwait(false);
        int sessions = await CountScopeAsync(connection, transaction, "sessions", scope, cancellationToken)
            .ConfigureAwait(false);
        int operations = await CountScopeAsync(connection, transaction, "operations", scope, cancellationToken)
            .ConfigureAwait(false);
        int turns = await CountScopeAsync(connection, transaction, "turns", scope, cancellationToken)
            .ConfigureAwait(false);
        int tools = await CountScopeAsync(connection, transaction, "tool_events", scope, cancellationToken)
            .ConfigureAwait(false);
        int resources = await CountScopeAsync(connection, transaction, "resource_samples", scope, cancellationToken)
            .ConfigureAwait(false);
        return new HistoryClearPreview(scope, requests, sessions, operations, turns, tools, resources);
    }

    private static async Task<int> CountScopeAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string table,
        HistoryClearScope scope,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {table}{BuildScopeWhere(command, table, scope)};";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task DeleteScopeAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        HistoryClearScope scope,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table}{BuildScopeWhere(command, table, scope)};";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildScopeWhere(SqliteCommand command, string table, HistoryClearScope scope)
    {
        if (scope.AllHistory)
        {
            return string.Empty;
        }

        string timestamp = table switch
        {
            "resource_samples" => "captured_at_utc",
            "sessions" or "operations" => "COALESCE(ended_at_utc, started_at_utc)",
            _ => "started_at_utc",
        };
        List<string> predicates = [];
        if (scope.From is not null)
        {
            predicates.Add($"{timestamp} >= $clear_from");
            command.Parameters.AddWithValue("$clear_from", ToDbTime(scope.From.Value));
        }

        if (scope.To is not null)
        {
            predicates.Add($"{timestamp} <= $clear_to");
            command.Parameters.AddWithValue("$clear_to", ToDbTime(scope.To.Value));
        }

        return " WHERE " + string.Join(" AND ", predicates);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", cancellationToken)
            .ConfigureAwait(false);
        return connection;
    }

    private async Task<SqliteConnection> OpenReadConnectionAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_readConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", cancellationToken)
            .ConfigureAwait(false);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateOperationGraph(TechnicalOperationGraph graph)
    {
        if (graph.Session is not null && graph.Operation.SessionId != graph.Session.SessionId)
        {
            throw new ArgumentException("Operation session must match the supplied session record.", nameof(graph));
        }

        if (graph.Turns.Any(item => item.OperationId != graph.Operation.OperationId) ||
            graph.ToolEvents.Any(item => item.OperationId != graph.Operation.OperationId) ||
            graph.ResourceSamples.Any(item => item.OperationId != graph.Operation.OperationId))
        {
            throw new ArgumentException("All graph records must reference the supplied operation.", nameof(graph));
        }

        if (graph.Turns.Any(item => item.Sequence < 0 || item.Duration < TimeSpan.Zero) ||
            graph.Turns.Select(item => item.Sequence).Distinct().Count() != graph.Turns.Count ||
            graph.ToolEvents.Any(item => item.TurnSequence < 0 || item.Sequence < 0 || item.Duration < TimeSpan.Zero))
        {
            throw new ArgumentException("Turn/tool sequence and duration values are invalid.", nameof(graph));
        }

        if (graph.Turns.Any(item =>
                item.AvailableToolCount.Unit != MetricUnit.Count ||
                item.InvokedToolCount.Unit != MetricUnit.Count) ||
            graph.ToolEvents.Any(item =>
                item.DurationMetric.Unit != MetricUnit.Milliseconds ||
                item.DurationMetric.Value != (decimal)item.Duration.TotalMilliseconds))
        {
            throw new ArgumentException("Turn/tool metrics have incompatible units or values.", nameof(graph));
        }
    }

    private static void ValidateFilter(HistoryFilter filter)
    {
        if (filter.From is not null && filter.To is not null && filter.From > filter.To)
        {
            throw new ArgumentException("History filter start cannot be after its end.", nameof(filter));
        }
    }

    private static void ValidateMetricMetadata(MetricValue metric)
    {
        if (TechnicalIdentifier.FromBackend(metric.SourceVersion) is null ||
            metric.DerivationVersion is not null && TechnicalIdentifier.FromBackend(metric.DerivationVersion) is null)
        {
            throw new ArgumentException("Metric provenance must be a bounded technical identifier.", nameof(metric));
        }
    }

    private static Dictionary<HistoryMetric, List<decimal>> GetBucket(
        Dictionary<DateOnly, Dictionary<HistoryMetric, List<decimal>>> buckets,
        DateOnly day)
    {
        if (!buckets.TryGetValue(day, out Dictionary<HistoryMetric, List<decimal>>? bucket))
        {
            bucket = [];
            buckets.Add(day, bucket);
        }

        return bucket;
    }

    private static void AddSample(
        Dictionary<HistoryMetric, List<decimal>> bucket,
        HistoryMetric metric,
        decimal value)
    {
        if (!bucket.TryGetValue(metric, out List<decimal>? samples))
        {
            samples = [];
            bucket.Add(metric, samples);
        }

        samples.Add(value);
    }

    private static void AddCountMetricParameters(
        SqliteCommand command,
        string prefix,
        MetricValue metric)
    {
        if (metric.Unit != MetricUnit.Count)
        {
            throw new ArgumentException("Tool counts must use the count unit.", nameof(metric));
        }

        ValidateMetricMetadata(metric);
        command.Parameters.AddWithValue($"${prefix}_value", DbValue(metric.Value));
        command.Parameters.AddWithValue($"${prefix}_quality", (int)metric.Quality);
        command.Parameters.AddWithValue($"${prefix}_source", (int)metric.Source);
        command.Parameters.AddWithValue($"${prefix}_source_version", metric.SourceVersion);
        command.Parameters.AddWithValue($"${prefix}_derivation_version", DbValue(metric.DerivationVersion));
    }

    private static IEnumerable<(string Key, MetricValue Metric)> ResourceMetrics(
        TechnicalResourceSampleRecord sample)
    {
        yield return (ResourceMetricSystemCpu, sample.CpuPercent);
        yield return (ResourceMetricSystemMemory, sample.MemoryPercent);
        yield return (ResourceMetricSystemMemoryUsed, sample.MemoryUsedBytes);
        yield return (ResourceMetricProcessCpu, sample.ProcessCpuPercent);
        yield return (ResourceMetricProcessMemory, sample.ProcessMemoryBytes);
        yield return (ResourceMetricDiskRead, sample.DiskReadBytes);
        yield return (ResourceMetricDiskWrite, sample.DiskWriteBytes);
        yield return (ResourceMetricClientToBackend, sample.ClientToBackendBytes);
        yield return (ResourceMetricBackendToClient, sample.BackendToClientBytes);
        yield return (ResourceMetricGpuUtilization, sample.GpuUtilizationPercent);
        yield return (ResourceMetricGpuVramUsed, sample.GpuVramUsedBytes);
        yield return (ResourceMetricGpuVramTotal, sample.GpuVramTotalBytes);
        yield return (ResourceMetricGpuTemperature, sample.GpuTemperatureCelsius);
        yield return (ResourceMetricGpuPower, sample.GpuPowerWatts);
    }

    private static MetricValue GetResourceMetric(
        Dictionary<string, MetricValue> metrics,
        string key,
        MetricValue fallback) =>
        metrics.TryGetValue(key, out MetricValue? metric) ? metric : fallback;

    private static MetricValue CreateStoredMetric(
        SqliteDataReader reader,
        MetricUnit unit,
        int valueOrdinal,
        int qualityOrdinal,
        int sourceOrdinal,
        int sourceVersionOrdinal,
        int derivationVersionOrdinal)
    {
        MetricQuality quality = (MetricQuality)reader.GetInt32(qualityOrdinal);
        decimal? value = reader.IsDBNull(valueOrdinal) ? null : reader.GetDecimal(valueOrdinal);
        return new MetricValue(
            value,
            unit,
            quality,
            (MetricSource)reader.GetInt32(sourceOrdinal),
            reader.GetString(sourceVersionOrdinal),
            reader.IsDBNull(derivationVersionOrdinal) ? null : reader.GetString(derivationVersionOrdinal));
    }

    private static MetricValue CreateStoredPercentMetric(
        SqliteDataReader reader,
        int valueOrdinal,
        int qualityOrdinal,
        int sourceOrdinal,
        int sourceVersionOrdinal,
        int derivationVersionOrdinal)
        => CreateStoredMetric(
            reader,
            MetricUnit.Percent,
            valueOrdinal,
            qualityOrdinal,
            sourceOrdinal,
            sourceVersionOrdinal,
            derivationVersionOrdinal);

    private static HistoryErrorType MapErrorType(ProxyOutcome outcome) => outcome switch
    {
        ProxyOutcome.Completed => HistoryErrorType.None,
        ProxyOutcome.BackendUnavailable => HistoryErrorType.BackendUnavailable,
        ProxyOutcome.ClientCancelled => HistoryErrorType.ClientCancelled,
        ProxyOutcome.RelayFailed => HistoryErrorType.RelayFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };

    private static string MetricKey(HistoryMetric metric) => metric switch
    {
        HistoryMetric.InputTokens => "input_tokens",
        HistoryMetric.OutputTokens => "output_tokens",
        HistoryMetric.TotalTokens => "total_tokens",
        HistoryMetric.CachedTokens => "cached_tokens",
        HistoryMetric.ReasoningTokens => "reasoning_tokens",
        HistoryMetric.ContextUsageTokens => "context_usage_tokens",
        HistoryMetric.ContextLimitTokens => "context_limit_tokens",
        HistoryMetric.ContextHistoryTokens => "context_history_tokens",
        HistoryMetric.ContextToolTokens => "context_tool_tokens",
        HistoryMetric.PromptTokensPerSecond => "prompt_tokens_per_second",
        HistoryMetric.GenerationTokensPerSecond => "generation_tokens_per_second",
        HistoryMetric.TimeToFirstTokenMilliseconds => "ttft_ms",
        HistoryMetric.ModelLoadMilliseconds => "model_load_ms",
        HistoryMetric.QueueMilliseconds => "queue_ms",
        HistoryMetric.TotalDurationMilliseconds => "total_duration_ms",
        _ => throw new ArgumentOutOfRangeException(nameof(metric)),
    };

    private static HistoryMetric ParseMetricKey(string key) => key switch
    {
        "input_tokens" => HistoryMetric.InputTokens,
        "output_tokens" => HistoryMetric.OutputTokens,
        "total_tokens" => HistoryMetric.TotalTokens,
        "cached_tokens" => HistoryMetric.CachedTokens,
        "reasoning_tokens" => HistoryMetric.ReasoningTokens,
        "context_usage_tokens" => HistoryMetric.ContextUsageTokens,
        "context_limit_tokens" => HistoryMetric.ContextLimitTokens,
        "context_history_tokens" => HistoryMetric.ContextHistoryTokens,
        "context_tool_tokens" => HistoryMetric.ContextToolTokens,
        "prompt_tokens_per_second" => HistoryMetric.PromptTokensPerSecond,
        "generation_tokens_per_second" => HistoryMetric.GenerationTokensPerSecond,
        "ttft_ms" => HistoryMetric.TimeToFirstTokenMilliseconds,
        "model_load_ms" => HistoryMetric.ModelLoadMilliseconds,
        "queue_ms" => HistoryMetric.QueueMilliseconds,
        "total_duration_ms" => HistoryMetric.TotalDurationMilliseconds,
        _ => throw new InvalidDataException("Unknown persisted metric key."),
    };

    private static TechnicalIdentifier ReadIdentifier(string value) =>
        TechnicalIdentifier.FromBackend(value) ??
        throw new InvalidDataException("Persisted technical identifier is invalid.");

    private static ModelLoadDisposition ReadModelLoadDisposition(int value) =>
        Enum.IsDefined((ModelLoadDisposition)value)
            ? (ModelLoadDisposition)value
            : throw new InvalidDataException("Persisted model-load disposition is invalid.");

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string ToDbTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseDbTime(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record RequestRow(
        Guid RequestId,
        Guid? SessionId,
        Guid? OperationId,
        DateTimeOffset StartedAt,
        int? HttpStatusCode,
        ProxyOutcome Outcome,
        HistoryErrorType ErrorType,
        ClientKind Client,
        BackendKind Backend,
        TechnicalIdentifier? Model,
        ModelLoadDisposition ModelLoadDisposition,
        Guid? CorrelatedTurnId,
        int? CorrelatedTurnSequence);

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static async Task RecordSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_migrations(version, applied_at_utc)
            VALUES ($version, $applied);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$applied", ToDbTime(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private const string SchemaSql = """
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
        """;

    private const string Migration2Sql = """
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
        """;

    private const string Migration3Sql = """
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
        """;

    private const string Migration4Sql = """
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
        """;

    private const string ResourceMetricSystemCpu = "system_cpu_percent";
    private const string ResourceMetricSystemMemory = "system_memory_percent";
    private const string ResourceMetricSystemMemoryUsed = "system_memory_used_bytes";
    private const string ResourceMetricProcessCpu = "process_cpu_percent";
    private const string ResourceMetricProcessMemory = "process_memory_bytes";
    private const string ResourceMetricDiskRead = "disk_read_bytes";
    private const string ResourceMetricDiskWrite = "disk_write_bytes";
    private const string ResourceMetricClientToBackend = "client_to_backend_bytes";
    private const string ResourceMetricBackendToClient = "backend_to_client_bytes";
    private const string ResourceMetricGpuUtilization = "gpu_utilization_percent";
    private const string ResourceMetricGpuVramUsed = "gpu_vram_used_bytes";
    private const string ResourceMetricGpuVramTotal = "gpu_vram_total_bytes";
    private const string ResourceMetricGpuTemperature = "gpu_temperature_celsius";
    private const string ResourceMetricGpuPower = "gpu_power_watts";
}
