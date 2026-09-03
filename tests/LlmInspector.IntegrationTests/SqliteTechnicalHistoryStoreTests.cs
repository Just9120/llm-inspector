using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace LlmInspector.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class SqliteTechnicalHistoryStoreTests
{
    [TestMethod]
    public async Task RequestHistoryPersistsTechnicalMetricsAndSupportsEveryFilterDimension()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset firstAt = new(2026, 1, 10, 12, 0, 0, TimeSpan.Zero);
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        ProxyObservation first = CreateObservation(
            firstId,
            firstAt,
            ClientKind.Cline,
            BackendKind.Ollama,
            "model-a",
            ProxyOutcome.Completed,
            10);
        ProxyObservation second = CreateObservation(
            secondId,
            firstAt.AddDays(1),
            ClientKind.OpenWebUi,
            BackendKind.LmStudio,
            "model-b",
            ProxyOutcome.BackendUnavailable,
            20);
        await fixture.Store.RecordAsync(first, CancellationToken.None);
        await fixture.Store.RecordAsync(second, CancellationToken.None);
        await fixture.Store.RecordOperationGraphAsync(new TechnicalOperationGraph(
            new TechnicalSessionRecord(
                sessionId,
                firstAt,
                firstAt.AddMinutes(1),
                ClientKind.Cline,
                BackendKind.Ollama,
                Id("model-a")),
            new TechnicalOperationRecord(
                operationId,
                sessionId,
                firstAt,
                firstAt.AddMinutes(1),
                ClientKind.Cline,
                BackendKind.Ollama,
                Id("model-a"),
                TechnicalOperationStatus.Completed,
                HistoryErrorType.None),
            [new TechnicalTurnRecord(
                Guid.NewGuid(),
                operationId,
                0,
                firstId,
                firstAt,
                TimeSpan.FromSeconds(1),
                ProxyOutcome.Completed,
                HistoryErrorType.None)],
            [],
            []));

        RequestHistoryItem stored = AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(SessionId: sessionId)));
        Assert.AreEqual(firstId, stored.RequestId);
        Assert.AreEqual(operationId, stored.OperationId);
        Assert.AreEqual(10m, stored.Metrics[HistoryMetric.InputTokens].Value);
        Assert.AreEqual(MetricQuality.Exact, stored.Metrics[HistoryMetric.InputTokens].Quality);
        Assert.AreEqual(250m, stored.Metrics[HistoryMetric.TotalDurationMilliseconds].Value);
        Assert.AreEqual(MetricQuality.Calculated, stored.Metrics[HistoryMetric.TotalDurationMilliseconds].Quality);

        Assert.AreEqual(firstId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(To: firstAt.AddHours(1)))).RequestId);
        Assert.AreEqual(firstId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(Client: ClientKind.Cline))).RequestId);
        Assert.AreEqual(firstId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(Backend: BackendKind.Ollama))).RequestId);
        Assert.AreEqual(firstId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(Model: Id("model-a")))).RequestId);
        Assert.AreEqual(firstId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(Status: ProxyOutcome.Completed))).RequestId);
        Assert.AreEqual(secondId, AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(ErrorType: HistoryErrorType.BackendUnavailable))).RequestId);
    }

    [TestMethod]
    public async Task OperationDetailReturnsOrderedTurnsToolsTimingsErrorsAndResources()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset startedAt = new(2026, 2, 1, 10, 0, 0, TimeSpan.Zero);
        Guid operationId = Guid.NewGuid();
        Guid firstRequestId = Guid.NewGuid();
        Guid secondRequestId = Guid.NewGuid();
        await fixture.Store.RecordAsync(CreateObservation(
            firstRequestId,
            startedAt,
            ClientKind.HermesDesktop,
            BackendKind.LlamaCpp,
            "model-c",
            ProxyOutcome.Completed,
            11), CancellationToken.None);
        await fixture.Store.RecordAsync(CreateObservation(
            secondRequestId,
            startedAt.AddSeconds(2),
            ClientKind.HermesDesktop,
            BackendKind.LlamaCpp,
            "model-c",
            ProxyOutcome.RelayFailed,
            12), CancellationToken.None);

        TechnicalOperationGraph graph = new(
            null,
            new TechnicalOperationRecord(
                operationId,
                null,
                startedAt,
                startedAt.AddSeconds(5),
                ClientKind.HermesDesktop,
                BackendKind.LlamaCpp,
                Id("model-c"),
                TechnicalOperationStatus.Error,
                HistoryErrorType.RelayFailed),
            [
                new TechnicalTurnRecord(
                    Guid.NewGuid(), operationId, 1, secondRequestId, startedAt.AddSeconds(2),
                    TimeSpan.FromSeconds(2), ProxyOutcome.RelayFailed, HistoryErrorType.RelayFailed)
                {
                    AvailableToolCount = Count(2),
                    InvokedToolCount = Count(1),
                },
                new TechnicalTurnRecord(
                    Guid.NewGuid(), operationId, 0, firstRequestId, startedAt,
                    TimeSpan.FromSeconds(1), ProxyOutcome.Completed, HistoryErrorType.None)
                {
                    AvailableToolCount = Count(4),
                    InvokedToolCount = Count(1),
                },
            ],
            [
                new TechnicalToolEventRecord(
                    Guid.NewGuid(), operationId, 1, 1, Id("read_file"), startedAt.AddSeconds(3),
                    TimeSpan.FromMilliseconds(40), TechnicalToolStatus.Error, HistoryErrorType.RelayFailed),
                new TechnicalToolEventRecord(
                    Guid.NewGuid(), operationId, 0, 0, Id("list_files"), startedAt.AddSeconds(1),
                    TimeSpan.FromMilliseconds(20), TechnicalToolStatus.Completed, HistoryErrorType.None),
            ],
            [
                new TechnicalResourceSampleRecord(
                    Guid.NewGuid(), operationId, startedAt.AddSeconds(4), Percent(70), Percent(80)),
                new TechnicalResourceSampleRecord(
                    Guid.NewGuid(), operationId, startedAt.AddSeconds(1), Percent(30), Percent(40))
                {
                    RequestId = firstRequestId,
                    Stage = RequestStageValue.ProtocolObserved(RequestStage.PromptProcessing, "resource-store-fixture-v1"),
                    RelatedProcess = new TechnicalProcessAssociation(
                        42,
                        startedAt.AddHours(-1),
                        Id("llama-server"),
                        "listener-owner-fixture-v1"),
                    GpuDeviceId = Id("GPU-fixture"),
                    MemoryUsedBytes = ResourceMetric(1_024, MetricUnit.Bytes, MetricSource.WindowsApi),
                    ProcessCpuPercent = ResourceMetric(25, MetricUnit.Percent, MetricSource.WindowsApi),
                    ProcessMemoryBytes = ResourceMetric(512, MetricUnit.Bytes, MetricSource.WindowsApi),
                    DiskReadBytes = ResourceMetric(100, MetricUnit.Bytes, MetricSource.WindowsApi),
                    DiskWriteBytes = ResourceMetric(200, MetricUnit.Bytes, MetricSource.WindowsApi),
                    ClientToBackendBytes = ResourceMetric(300, MetricUnit.Bytes, MetricSource.GatewayTraffic),
                    BackendToClientBytes = ResourceMetric(400, MetricUnit.Bytes, MetricSource.GatewayTraffic),
                    GpuUtilizationPercent = ResourceMetric(50, MetricUnit.Percent, MetricSource.NvidiaSmi),
                    GpuVramUsedBytes = ResourceMetric(2_048, MetricUnit.Bytes, MetricSource.NvidiaSmi),
                    GpuVramTotalBytes = ResourceMetric(4_096, MetricUnit.Bytes, MetricSource.NvidiaSmi),
                    GpuTemperatureCelsius = ResourceMetric(70, MetricUnit.Celsius, MetricSource.NvidiaSmi),
                    GpuPowerWatts = ResourceMetric(125, MetricUnit.Watts, MetricSource.NvidiaSmi),
                },
            ]);
        await fixture.Store.RecordOperationGraphAsync(graph);

        TechnicalOperationDetail? detail = await fixture.Store.GetOperationDetailAsync(operationId);

        Assert.IsNotNull(detail);
        Assert.AreEqual(TechnicalOperationStatus.Error, detail.Operation.Status);
        Assert.AreEqual(HistoryErrorType.RelayFailed, detail.Operation.ErrorType);
        Assert.AreEqual(0, detail.Turns[0].Sequence);
        Assert.AreEqual(1, detail.Turns[1].Sequence);
        Assert.AreEqual(4m, detail.Turns[0].AvailableToolCount.Value);
        Assert.AreEqual(1m, detail.Turns[0].InvokedToolCount.Value);
        Assert.AreEqual("list_files", detail.ToolEvents[0].ToolName.Value);
        Assert.AreEqual("read_file", detail.ToolEvents[1].ToolName.Value);
        Assert.AreEqual(30m, detail.ResourceSamples[0].CpuPercent.Value);
        Assert.AreEqual(firstRequestId, detail.ResourceSamples[0].RequestId);
        Assert.AreEqual(RequestStage.PromptProcessing, detail.ResourceSamples[0].Stage?.Stage);
        Assert.AreEqual(42, detail.ResourceSamples[0].RelatedProcess?.ProcessId);
        Assert.AreEqual("llama-server", detail.ResourceSamples[0].RelatedProcess?.ImageName.Value);
        Assert.AreEqual("GPU-fixture", detail.ResourceSamples[0].GpuDeviceId?.Value);
        Assert.AreEqual(100m, detail.ResourceSamples[0].DiskReadBytes.Value);
        Assert.AreEqual(400m, detail.ResourceSamples[0].BackendToClientBytes.Value);
        Assert.AreEqual(70m, detail.ResourceSamples[0].GpuTemperatureCelsius.Value);
        Assert.AreEqual(70m, detail.ResourceSamples[1].CpuPercent.Value);
        Assert.AreEqual(TimeSpan.FromMilliseconds(40), detail.ToolEvents[1].Duration);
        Assert.AreEqual(MetricQuality.Calculated, detail.ToolEvents[1].DurationMetric.Quality);
    }

    [TestMethod]
    public async Task AnalyticsBuildsTrendsAndComparesPeriodsModelsBackendsAndClients()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset baselineDay = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset candidateDay = baselineDay.AddDays(1);
        for (int index = 0; index < 3; index++)
        {
            await fixture.Store.RecordAsync(CreateObservation(
                Guid.NewGuid(), baselineDay.AddMinutes(index), ClientKind.Cline, BackendKind.Ollama,
                "baseline-model", ProxyOutcome.Completed, 10 + index), CancellationToken.None);
            await fixture.Store.RecordAsync(CreateObservation(
                Guid.NewGuid(), candidateDay.AddMinutes(index), ClientKind.OpenWebUi, BackendKind.LmStudio,
                "candidate-model", index == 2 ? ProxyOutcome.RelayFailed : ProxyOutcome.Completed, 30 + index),
                CancellationToken.None);
        }

        Guid operationId = Guid.NewGuid();
        await fixture.Store.RecordOperationGraphAsync(new TechnicalOperationGraph(
            null,
            new TechnicalOperationRecord(
                operationId, null, candidateDay, candidateDay.AddMinutes(3), ClientKind.OpenWebUi,
                BackendKind.LmStudio, Id("candidate-model"), TechnicalOperationStatus.Completed, HistoryErrorType.None),
            [],
            [],
            [
                new TechnicalResourceSampleRecord(Guid.NewGuid(), operationId, candidateDay, Percent(30), Percent(40)),
                new TechnicalResourceSampleRecord(Guid.NewGuid(), operationId, candidateDay.AddMinutes(1), Percent(40), Percent(50)),
                new TechnicalResourceSampleRecord(Guid.NewGuid(), operationId, candidateDay.AddMinutes(2), Percent(50), Percent(60)),
            ]));

        PeriodAnalytics analytics = await fixture.Store.AnalyzePeriodAsync(new HistoryFilter(
            From: baselineDay,
            To: candidateDay.AddDays(1)));
        Assert.AreEqual(2, analytics.Trend.Count);
        AnalyticsTrendPoint candidate = analytics.Trend[1];
        HistoryMetric[] requiredTrendMetrics =
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
        CollectionAssert.AreEquivalent(requiredTrendMetrics, candidate.Metrics.Keys.ToArray());
        Assert.AreEqual(3, candidate.Metrics[HistoryMetric.InputTokens].SampleCount);
        Assert.IsTrue(candidate.Metrics[HistoryMetric.InputTokens].IsStatisticallySufficient);
        Assert.AreEqual(40m, candidate.Metrics[HistoryMetric.CpuPercent].ArithmeticMean);

        HistoryFilter baselinePeriod = new(From: baselineDay, To: baselineDay.AddHours(1));
        HistoryFilter candidatePeriod = new(From: candidateDay, To: candidateDay.AddHours(1));
        Assert.IsTrue((await fixture.Store.CompareAsync(
            baselinePeriod, candidatePeriod, HistoryMetric.TimeToFirstTokenMilliseconds)).IsConfirmedDegradation);
        Assert.AreEqual(3, (await fixture.Store.CompareAsync(
            new HistoryFilter(Model: Id("baseline-model")),
            new HistoryFilter(Model: Id("candidate-model")),
            HistoryMetric.TotalDurationMilliseconds)).Candidate.SampleCount);
        Assert.AreEqual(3, (await fixture.Store.CompareAsync(
            new HistoryFilter(Backend: BackendKind.Ollama),
            new HistoryFilter(Backend: BackendKind.LmStudio),
            HistoryMetric.InputTokens)).Baseline.SampleCount);
        Assert.AreEqual(3, (await fixture.Store.CompareAsync(
            new HistoryFilter(Client: ClientKind.Cline),
            new HistoryFilter(Client: ClientKind.OpenWebUi),
            HistoryMetric.ErrorRatePercent)).Candidate.SampleCount);
    }

    [TestMethod]
    public async Task CorrelationAndModelLoadClassificationArePersistedAndAnalyzed()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset startedAt = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
        Guid sessionId = Guid.NewGuid();
        Guid coldTurnId = Guid.NewGuid();
        Guid warmTurnId = Guid.NewGuid();
        ProxyObservation cold = CreateObservation(
            Guid.NewGuid(), startedAt, ClientKind.Cline, BackendKind.LmStudio,
            "native-model", ProxyOutcome.Completed, 10,
            ModelLoadDisposition.Cold,
            new RequestCorrelation(sessionId, coldTurnId, 1));
        ProxyObservation warm = CreateObservation(
            Guid.NewGuid(), startedAt.AddMinutes(1), ClientKind.Cline, BackendKind.LmStudio,
            "native-model", ProxyOutcome.Completed, 20,
            ModelLoadDisposition.Warm,
            new RequestCorrelation(sessionId, warmTurnId, 2));
        ProxyObservation unavailable = CreateObservation(
            Guid.NewGuid(), startedAt.AddMinutes(2), ClientKind.Cline, BackendKind.LmStudio,
            "native-model", ProxyOutcome.Completed, 30);

        await fixture.Store.RecordAsync(cold, CancellationToken.None);
        await fixture.Store.RecordAsync(warm, CancellationToken.None);
        await fixture.Store.RecordAsync(unavailable, CancellationToken.None);

        IReadOnlyList<RequestHistoryItem> correlated = await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(SessionId: sessionId));
        Assert.HasCount(2, correlated);
        RequestHistoryItem storedWarm = correlated.Single(item => item.CorrelatedTurnSequence == 2);
        Assert.AreEqual(warmTurnId, storedWarm.CorrelatedTurnId);
        Assert.AreEqual(ModelLoadDisposition.Warm, storedWarm.ModelLoadDisposition);

        PeriodAnalytics analytics = await fixture.Store.AnalyzePeriodAsync(new HistoryFilter(
            From: startedAt,
            To: startedAt.AddHours(1)));
        Assert.AreEqual(1, analytics.ModelLoads.ColdRequests);
        Assert.AreEqual(1, analytics.ModelLoads.WarmRequests);
        Assert.AreEqual(1, analytics.ModelLoads.UnavailableRequests);
        Assert.AreEqual(3, analytics.ModelLoads.TotalRequests);
        Assert.HasCount(1, analytics.Trend);
        Assert.AreEqual(
            3,
            analytics.Trend[0].Metrics[HistoryMetric.ModelLoadMilliseconds].SampleCount);
    }

    [TestMethod]
    public async Task TypedRecurringErrorsArePersistedCorrelatedAndComparedByPeriod()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset baselineAt = new(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset candidateAt = baselineAt.AddDays(1);
        Guid sessionId = Guid.NewGuid();
        ProxyObservation[] observations =
        [
            CreateObservation(
                Guid.NewGuid(), baselineAt, ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.BackendUnavailable, 10,
                correlation: new RequestCorrelation(sessionId, Guid.NewGuid(), 1),
                errorType: ProxyErrorType.ConnectionRefused),
            CreateObservation(
                Guid.NewGuid(), baselineAt.AddMinutes(1), ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.ClientCancelled, 10,
                correlation: new RequestCorrelation(sessionId, Guid.NewGuid(), 2),
                errorType: ProxyErrorType.ClientCancellation),
            CreateObservation(
                Guid.NewGuid(), baselineAt.AddMinutes(2), ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.BackendUnavailable, 10,
                errorType: ProxyErrorType.ConnectionRefused),
            CreateObservation(
                Guid.NewGuid(), candidateAt, ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.Completed, 10,
                errorType: ProxyErrorType.ContextOverflow),
            CreateObservation(
                Guid.NewGuid(), candidateAt.AddMinutes(1), ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.Completed, 10,
                errorType: ProxyErrorType.ContextOverflow),
            CreateObservation(
                Guid.NewGuid(), candidateAt.AddMinutes(2), ClientKind.Cline, BackendKind.Ollama,
                "model", ProxyOutcome.Completed, 10),
        ];
        foreach (ProxyObservation observation in observations)
        {
            await fixture.Store.RecordAsync(observation, CancellationToken.None);
        }

        HistoryFilter baseline = new(From: baselineAt, To: baselineAt.AddHours(1));
        HistoryFilter candidate = new(From: candidateAt, To: candidateAt.AddHours(1));
        IReadOnlyList<RequestHistoryItem> stored = await fixture.Store.QueryRequestsAsync(baseline);
        RequestHistoryItem recurringConnection = stored.First(item => item.ErrorType == HistoryErrorType.ConnectionRefused);
        Assert.IsTrue(recurringConnection.IsRecurringError);
        Assert.AreEqual(2, recurringConnection.ErrorGroupOccurrenceCount);

        PeriodAnalytics analytics = await fixture.Store.AnalyzePeriodAsync(baseline);
        Assert.IsTrue(analytics.ErrorGroups.Single(item => item.ErrorType == HistoryErrorType.ConnectionRefused).IsRecurring);
        CorrelatedErrorGroup correlation = analytics.ErrorCorrelations.ConfirmedGroups.Single();
        Assert.AreEqual(ErrorCorrelationBasis.Session, correlation.Basis);
        Assert.AreEqual(sessionId, correlation.CorrelationId);
        Assert.AreEqual(1, analytics.ErrorCorrelations.UncorrelatedErrors);

        AnalyticsComparison comparison = await fixture.Store.CompareAsync(
            baseline,
            candidate,
            HistoryMetric.ErrorRatePercent);
        CollectionAssert.AreEquivalent(
            new[] { HistoryErrorType.ConnectionRefused, HistoryErrorType.ContextOverflow },
            comparison.RecurringErrorFrequency.Select(item => item.ErrorType).ToArray());
        ErrorFrequencyComparison contextChange = comparison.RecurringErrorFrequency.Single(
            item => item.ErrorType == HistoryErrorType.ContextOverflow);
        Assert.AreEqual(0, contextChange.BaselineOccurrences);
        Assert.AreEqual(2, contextChange.CandidateOccurrences);
    }

    [TestMethod]
    public async Task VersionOneDatabaseMigratesToCurrentSchemaWithoutLosingRequests()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-migration-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(directory, "history.db");
        Directory.CreateDirectory(directory);
        Guid requestId = Guid.NewGuid();
        try
        {
            await using (SqliteConnection connection = new($"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE schema_migrations(
                        version INTEGER PRIMARY KEY,
                        applied_at_utc TEXT NOT NULL
                    ) STRICT;
                    INSERT INTO schema_migrations(version, applied_at_utc)
                    VALUES (1, '2026-01-01T00:00:00.0000000+00:00');
                    CREATE TABLE requests(
                        request_id TEXT PRIMARY KEY CHECK(length(request_id) = 32),
                        session_id TEXT NULL,
                        operation_id TEXT NULL,
                        started_at_utc TEXT NOT NULL,
                        http_status_code INTEGER NULL,
                        outcome INTEGER NOT NULL,
                        error_type INTEGER NOT NULL,
                        client INTEGER NOT NULL,
                        backend INTEGER NOT NULL,
                        model TEXT NULL CHECK(model IS NULL OR length(model) <= 128)
                    ) STRICT;
                    INSERT INTO requests(
                        request_id, session_id, operation_id, started_at_utc, http_status_code,
                        outcome, error_type, client, backend, model)
                    VALUES ($id, NULL, NULL, '2026-01-01T00:00:00.0000000+00:00', 200, 0, 0, 0, 0, 'legacy-model');
                    """;
                command.Parameters.AddWithValue("$id", requestId.ToString("N"));
                await command.ExecuteNonQueryAsync();
            }

            await using (SqliteTechnicalHistoryStore store = new(databasePath))
            {
                await store.InitializeAsync();
                RequestHistoryItem migrated = AssertSingle(await store.QueryRequestsAsync(new HistoryFilter()));
                Assert.AreEqual(requestId, migrated.RequestId);
                Assert.AreEqual(ModelLoadDisposition.Unavailable, migrated.ModelLoadDisposition);
                Assert.IsNull(migrated.CorrelatedTurnId);
                Assert.IsNull(migrated.CorrelatedTurnSequence);
            }

            await using SqliteConnection verification = new($"Data Source={databasePath};Mode=ReadOnly");
            await verification.OpenAsync();
            await using SqliteCommand versionCommand = verification.CreateCommand();
            versionCommand.CommandText = "SELECT MAX(version) FROM schema_migrations;";
            Assert.AreEqual(4L, await versionCommand.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow((int)HistoryRetention.SevenDays, 8)]
    [DataRow((int)HistoryRetention.ThirtyDays, 31)]
    [DataRow((int)HistoryRetention.NinetyDays, 91)]
    public async Task FiniteRetentionDeletesOnlyRecordsOlderThanSelectedBoundary(int retentionValue, int oldDays)
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset now = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        Guid oldId = Guid.NewGuid();
        Guid newId = Guid.NewGuid();
        await fixture.Store.RecordAsync(CreateObservation(
            oldId, now.AddDays(-oldDays), ClientKind.Cline, BackendKind.Ollama,
            "retention-model", ProxyOutcome.Completed, 10), CancellationToken.None);
        await fixture.Store.RecordAsync(CreateObservation(
            newId, now.AddDays(-1), ClientKind.Cline, BackendKind.Ollama,
            "retention-model", ProxyOutcome.Completed, 20), CancellationToken.None);

        int deleted = await fixture.Store.ApplyRetentionAsync((HistoryRetention)retentionValue, now);
        IReadOnlyList<RequestHistoryItem> remaining = await fixture.Store.QueryRequestsAsync(new HistoryFilter());

        Assert.AreEqual(1, deleted);
        Assert.AreEqual(newId, AssertSingle(remaining).RequestId);
    }

    [TestMethod]
    public async Task IndefiniteRetentionNeverDeletesRecords()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset now = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        await fixture.Store.RecordAsync(CreateObservation(
            Guid.NewGuid(), now.AddYears(-10), ClientKind.Cline, BackendKind.Ollama,
            "retention-model", ProxyOutcome.Completed, 10), CancellationToken.None);

        Assert.AreEqual(0, await fixture.Store.ApplyRetentionAsync(HistoryRetention.Indefinite, now));
        Assert.AreEqual(1, (await fixture.Store.QueryRequestsAsync(new HistoryFilter())).Count);
    }

    [TestMethod]
    public async Task RetentionProcessesMoreThanOneOldestFirstBatch()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset now = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        for (int index = 0; index <= HistoryPolicies.RetentionDeleteBatchSize; index++)
        {
            await fixture.Store.RecordAsync(CreateObservation(
                Guid.NewGuid(), now.AddDays(-40).AddMinutes(index), ClientKind.Cline, BackendKind.Ollama,
                "retention-model", ProxyOutcome.Completed, 10), CancellationToken.None);
        }

        Guid boundaryId = Guid.NewGuid();
        await fixture.Store.RecordAsync(CreateObservation(
            boundaryId, now.AddDays(-30), ClientKind.Cline, BackendKind.Ollama,
            "retention-model", ProxyOutcome.Completed, 20), CancellationToken.None);

        int deleted = await fixture.Store.ApplyRetentionAsync(HistoryRetention.ThirtyDays, now);

        Assert.AreEqual(HistoryPolicies.RetentionDeleteBatchSize + 1, deleted);
        Assert.AreEqual(boundaryId, AssertSingle(await fixture.Store.QueryRequestsAsync(new HistoryFilter())).RequestId);
    }

    [TestMethod]
    public async Task RetentionSettingDefaultsToThirtyDaysAndPersistsSelection()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();

        Assert.AreEqual(HistoryRetention.ThirtyDays, await fixture.Store.GetRetentionAsync());
        await fixture.Store.SetRetentionAsync(HistoryRetention.NinetyDays);
        Assert.AreEqual(HistoryRetention.NinetyDays, await fixture.Store.GetRetentionAsync());
    }

    [TestMethod]
    public async Task BufferedSinkNeverWaitsForStorageAndReportsDroppedRecords()
    {
        BlockingHistoryStore store = new();
        await using BufferedTechnicalHistorySink sink = new(store, capacity: 1);
        ProxyObservation first = CreateObservation(
            Guid.NewGuid(), DateTimeOffset.UtcNow, ClientKind.Cline, BackendKind.Ollama,
            "buffer-model", ProxyOutcome.Completed, 10);
        ProxyObservation second = first with { RequestId = Guid.NewGuid() };
        ProxyObservation third = first with { RequestId = Guid.NewGuid() };

        await sink.RecordAsync(first, CancellationToken.None);
        await store.FirstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await sink.RecordAsync(second, CancellationToken.None);
        await sink.RecordAsync(third, CancellationToken.None);

        Assert.AreEqual(1, sink.DroppedCount);
        store.ReleaseWrites.TrySetResult();

        await sink.DisposeAsync();
        Assert.AreEqual(2, store.RecordedCount);
        Assert.AreEqual(0, sink.FailedCount);
    }

    [TestMethod]
    public async Task BufferedSinkPreservesObservationBeforeItsOperationGraph()
    {
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        Guid requestId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        ProxyObservation observation = CreateObservation(
            requestId, startedAt, ClientKind.Cline, BackendKind.Ollama,
            "buffer-operation-model", ProxyOutcome.Completed, 10);
        TechnicalOperationGraph graph = new(
            new TechnicalSessionRecord(
                sessionId, startedAt, startedAt.AddMilliseconds(50), ClientKind.Cline,
                BackendKind.Ollama, Id("buffer-operation-model")),
            new TechnicalOperationRecord(
                operationId, sessionId, startedAt, startedAt.AddMilliseconds(50), ClientKind.Cline,
                BackendKind.Ollama, Id("buffer-operation-model"),
                TechnicalOperationStatus.Completed, HistoryErrorType.None),
            [new TechnicalTurnRecord(
                Guid.NewGuid(), operationId, 1, requestId, startedAt,
                TimeSpan.FromMilliseconds(50), ProxyOutcome.Completed, HistoryErrorType.None)],
            [],
            []);

        await using (BufferedTechnicalHistorySink sink = new(fixture.Store))
        {
            await sink.RecordAsync(observation, CancellationToken.None);
            await sink.RecordOperationGraphAsync(graph, CancellationToken.None);
        }

        TechnicalOperationDetail? stored = await fixture.Store.GetOperationDetailAsync(operationId);
        RequestHistoryItem linked = AssertSingle(await fixture.Store.QueryRequestsAsync(
            new HistoryFilter(SessionId: sessionId)));
        Assert.IsNotNull(stored);
        Assert.AreEqual(requestId, stored.Turns.Single().RequestId);
        Assert.AreEqual(operationId, linked.OperationId);
    }

    [TestMethod]
    public async Task ManualClearRequiresExplicitScopeFreshPreviewAndConfirmation()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => new HistoryClearScope(allHistory: false));
        await using StoreFixture fixture = await StoreFixture.CreateAsync();
        DateTimeOffset oldAt = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        Guid oldId = Guid.NewGuid();
        Guid newId = Guid.NewGuid();
        await fixture.Store.RecordAsync(CreateObservation(
            oldId, oldAt, ClientKind.Cline, BackendKind.Ollama,
            "clear-model", ProxyOutcome.Completed, 10), CancellationToken.None);
        HistoryClearScope oldScope = new(allHistory: false, to: oldAt.AddHours(1));
        HistoryClearPreview preview = await fixture.Store.PreviewClearAsync(oldScope);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Store.ClearAsync(preview, confirmed: false));

        await fixture.Store.RecordAsync(CreateObservation(
            newId, oldAt.AddDays(1), ClientKind.Cline, BackendKind.Ollama,
            "clear-model", ProxyOutcome.Completed, 20), CancellationToken.None);
        HistoryClearPreview cleared = await fixture.Store.ClearAsync(preview, confirmed: true);

        Assert.AreEqual(1, cleared.Requests);
        Assert.AreEqual(newId, AssertSingle(await fixture.Store.QueryRequestsAsync(new HistoryFilter())).RequestId);

        HistoryClearPreview allPreview = await fixture.Store.PreviewClearAsync(new HistoryClearScope(allHistory: true));
        await fixture.Store.RecordAsync(CreateObservation(
            Guid.NewGuid(), oldAt.AddDays(2), ClientKind.Cline, BackendKind.Ollama,
            "clear-model", ProxyOutcome.Completed, 30), CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => fixture.Store.ClearAsync(allPreview, confirmed: true));
    }

    private static RequestHistoryItem AssertSingle(IReadOnlyList<RequestHistoryItem> items)
    {
        Assert.HasCount(1, items);
        return items[0];
    }

    private static ProxyObservation CreateObservation(
        Guid id,
        DateTimeOffset startedAt,
        ClientKind client,
        BackendKind backend,
        string model,
        ProxyOutcome outcome,
        decimal value,
        ModelLoadDisposition modelLoadDisposition = ModelLoadDisposition.Unavailable,
        RequestCorrelation? correlation = null,
        ProxyErrorType errorType = ProxyErrorType.None)
    {
        string sourceVersion = "history-fixture-v1";
        BackendResponseTelemetry telemetry = new(
            backend,
            Id(model),
            Exact(value, MetricUnit.TokenCount),
            Exact(value + 1, MetricUnit.TokenCount),
            Exact((value * 2) + 1, MetricUnit.TokenCount),
            Exact(2, MetricUnit.TokenCount),
            Exact(1, MetricUnit.TokenCount),
            Exact(value, MetricUnit.TokenCount),
            Exact(4096, MetricUnit.TokenCount),
            Exact(value - 1, MetricUnit.TokenCount),
            Exact(1, MetricUnit.TokenCount),
            Exact(100 - value, MetricUnit.TokensPerSecond),
            Exact(80 - value, MetricUnit.TokensPerSecond),
            Exact(value * 5, MetricUnit.Milliseconds),
            Exact(value, MetricUnit.Milliseconds),
            [],
            modelLoadDisposition);
        return new ProxyObservation(
            id,
            startedAt,
            TimeSpan.FromMilliseconds(250),
            outcome == ProxyOutcome.Completed ? 200 : 503,
            outcome,
            client,
            telemetry,
            MetricValue.Exact(value * 10, MetricUnit.Milliseconds, MetricSource.Inspector, sourceVersion))
        {
            Correlation = correlation,
            ErrorType = errorType,
        };

        MetricValue Exact(decimal metricValue, MetricUnit unit) =>
            MetricValue.Exact(metricValue, unit, MetricSource.BackendExtension, sourceVersion);
    }

    private static MetricValue Percent(decimal value) =>
        MetricValue.Exact(value, MetricUnit.Percent, MetricSource.Inspector, "resource-fixture-v1");

    private static MetricValue Count(decimal value) =>
        MetricValue.Exact(value, MetricUnit.Count, MetricSource.Inspector, "agent-operation-fixture-v1");

    private static MetricValue ResourceMetric(decimal value, MetricUnit unit, MetricSource source) =>
        MetricValue.Exact(value, unit, source, "resource-store-fixture-v1");

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid test identifier.");

    private sealed class StoreFixture : IAsyncDisposable
    {
        private StoreFixture(string directory, SqliteTechnicalHistoryStore store)
        {
            Directory = directory;
            Store = store;
        }

        public string Directory { get; }

        public SqliteTechnicalHistoryStore Store { get; }

        public static async Task<StoreFixture> CreateAsync()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-history-{Guid.NewGuid():N}");
            SqliteTechnicalHistoryStore store = new(Path.Combine(directory, "history.db"));
            await store.InitializeAsync();
            return new StoreFixture(directory, store);
        }

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class BlockingHistoryStore : ITechnicalHistoryStore
    {
        private int _recordedCount;

        public TaskCompletionSource FirstWriteStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseWrites { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RecordedCount => Volatile.Read(ref _recordedCount);

        public async ValueTask RecordAsync(ProxyObservation observation, CancellationToken cancellationToken)
        {
            FirstWriteStarted.TrySetResult();
            await ReleaseWrites.Task.WaitAsync(cancellationToken);
            Interlocked.Increment(ref _recordedCount);
        }

        public Task RecordOperationGraphAsync(
            TechnicalOperationGraph graph,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordResourceSamplesAsync(
            IReadOnlyList<TechnicalResourceSampleRecord> samples,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RequestHistoryItem>> QueryRequestsAsync(
            HistoryFilter filter,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TechnicalOperationDetail?> GetOperationDetailAsync(
            Guid operationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodAnalytics> AnalyzePeriodAsync(
            HistoryFilter filter,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AnalyticsComparison> CompareAsync(
            HistoryFilter baseline,
            HistoryFilter candidate,
            HistoryMetric metric,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryRetention> GetRetentionAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SetRetentionAsync(
            HistoryRetention retention,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> ApplyRetentionAsync(
            HistoryRetention retention,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryClearPreview> PreviewClearAsync(
            HistoryClearScope scope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<HistoryClearPreview> ClearAsync(
            HistoryClearPreview preview,
            bool confirmed,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
