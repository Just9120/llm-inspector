using LlmInspector.Application;
using LlmInspector.Diagnostics;
using LlmInspector.Domain;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class HistoryUiTests
{
    [TestMethod]
    public void FilterInputMapsEverySupportedDimensionToTypedContract()
    {
        Guid sessionId = Guid.NewGuid();

        HistoryFilter filter = App.HistoryUiParser.CreateFilter(
            "2026-01-01T00:00:00Z",
            "2026-01-02T00:00:00Z",
            nameof(ClientKind.Cline),
            nameof(BackendKind.Ollama),
            "model-a",
            sessionId.ToString(),
            nameof(ProxyOutcome.Completed),
            nameof(HistoryErrorType.None));

        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), filter.From);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), filter.To);
        Assert.AreEqual(ClientKind.Cline, filter.Client);
        Assert.AreEqual(BackendKind.Ollama, filter.Backend);
        Assert.AreEqual("model-a", filter.Model?.Value);
        Assert.AreEqual(sessionId, filter.SessionId);
        Assert.AreEqual(ProxyOutcome.Completed, filter.Status);
        Assert.AreEqual(HistoryErrorType.None, filter.ErrorType);
    }

    [TestMethod]
    [DataRow("Period", "2026-01-01T00:00:00Z..2026-01-02T00:00:00Z", "2026-02-01T00:00:00Z..2026-02-02T00:00:00Z")]
    [DataRow("Model", "model-a", "model-b")]
    [DataRow("Backend", "Ollama", "LmStudio")]
    [DataRow("Client", "Cline", "OpenWebUi")]
    public void ComparisonInputSupportsPeriodsModelsBackendsAndClients(
        string dimension,
        string baseline,
        string candidate)
    {
        App.HistoryComparisonFilters filters = App.HistoryUiParser.CreateComparisonFilters(
            dimension,
            baseline,
            candidate);

        Assert.AreNotEqual(filters.Baseline, filters.Candidate);
    }

    [TestMethod]
    public void InvalidHistoryAndClearInputsFailClosed()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.HistoryUiParser.CreateFilter(
            "not-a-time", null, "Any", "Any", null, null, "Any", "Any"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.HistoryUiParser.CreateComparisonFilters(
            "Period", "missing-range", "also-missing"));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.HistoryUiParser.CreateClearScope(
            allHistory: false, null, null));
    }

    [TestMethod]
    public void RetentionUiUsesTheFourExactUserFacingOptions()
    {
        Assert.HasCount(4, App.HistoryUiCatalog.RetentionChoices);
        Assert.AreEqual("7 days", App.HistoryUiCatalog.RetentionChoices[0].Label);
        Assert.AreEqual("30 days", App.HistoryUiCatalog.RetentionChoices[1].Label);
        Assert.AreEqual("90 days", App.HistoryUiCatalog.RetentionChoices[2].Label);
        Assert.AreEqual("indefinite", App.HistoryUiCatalog.RetentionChoices[3].Label);
    }

    [TestMethod]
    public void DiagnosticSnapshotUiCreatesExactRangeAndOperationSelections()
    {
        DiagnosticSnapshotSelection range = App.DiagnosticSnapshotUi.CreateSelection(
            App.DiagnosticSnapshotUi.TimeRangeScope,
            "2026-01-01T00:00:00Z",
            "2026-01-02T00:00:00Z",
            null);
        Guid operationId = Guid.NewGuid();
        DiagnosticSnapshotSelection operation = App.DiagnosticSnapshotUi.CreateSelection(
            App.DiagnosticSnapshotUi.OperationScope,
            null,
            null,
            operationId.ToString());

        Assert.AreEqual(DiagnosticSnapshotScope.TimeRange, range.Scope);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), range.FromUtc);
        Assert.AreEqual(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), range.ToUtc);
        Assert.AreEqual(DiagnosticSnapshotScope.Operation, operation.Scope);
        Assert.AreEqual(operationId, operation.OperationId);
        StringAssert.EndsWith(
            App.DiagnosticSnapshotUi.CreateDefaultLocalPath(DateTimeOffset.UnixEpoch),
            ".json",
            StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void DiagnosticSnapshotUiRejectsIncompleteOrInvalidScopeInput()
    {
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.DiagnosticSnapshotUi.CreateSelection(
            App.DiagnosticSnapshotUi.TimeRangeScope, null, null, Guid.NewGuid().ToString()));
        _ = Assert.ThrowsExactly<ArgumentException>(() => App.DiagnosticSnapshotUi.CreateSelection(
            App.DiagnosticSnapshotUi.OperationScope,
            "2026-01-01T00:00:00Z",
            "2026-01-02T00:00:00Z",
            "not-a-guid"));
    }

    [TestMethod]
    public void PresentersExposeHistoryStatisticsOperationDetailAndClearScope()
    {
        Guid sessionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        RequestHistoryItem request = new(
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            sessionId,
            operationId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            200,
            ProxyOutcome.Completed,
            HistoryErrorType.None,
            ClientKind.Cline,
            BackendKind.Ollama,
            Id("model-a"),
            new Dictionary<HistoryMetric, MetricValue>(),
            ModelLoadDisposition.Warm,
            Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"),
            2);
        string requests = App.HistoryTextPresenter.FormatRequests([request]);
        StringAssert.Contains(requests, "01234567");
        StringAssert.Contains(requests, "session=");
        StringAssert.Contains(requests, "turn=fedcba9876543210fedcba9876543210/2");
        StringAssert.Contains(requests, "operation=");
        StringAssert.Contains(requests, "model-load=Warm");

        RequestHistoryItem recurring = request with
        {
            ErrorType = HistoryErrorType.ConnectionRefused,
            ErrorOrigin = HistoryErrorOrigin.Backend,
            ErrorGroupOccurrenceCount = 2,
        };
        StringAssert.Contains(
            App.HistoryTextPresenter.FormatRequests([recurring]),
            "ConnectionRefused [recurring x2]");

        MetricAggregate aggregate = new(3, true, 20m, 20m, 30m);
        TechnicalRuntimeFacts baselineFacts = new(Id("config-a")) { BackendVersion = Id("backend-1") };
        TechnicalRuntimeFacts candidateFacts = new(Id("config-b")) { BackendVersion = Id("backend-2") };
        RuntimeConfigurationAggregate baselineConfiguration = new(
            baselineFacts,
            request.StartedAt,
            request.StartedAt.AddMinutes(2),
            3,
            new Dictionary<HistoryMetric, MetricAggregate>());
        RuntimeConfigurationAggregate candidateConfiguration = new(
            candidateFacts,
            request.StartedAt.AddDays(1),
            request.StartedAt.AddDays(1).AddMinutes(2),
            3,
            new Dictionary<HistoryMetric, MetricAggregate>());
        PeriodAnalytics analytics = new(
            new HistoryFilter(),
            [new AnalyticsTrendPoint(
                new DateOnly(2026, 1, 1),
                new Dictionary<HistoryMetric, MetricAggregate>
                {
                    [HistoryMetric.TimeToFirstTokenMilliseconds] = aggregate,
                })],
            new ModelLoadBreakdown(1, 2, 3))
        {
            ErrorGroups =
            [
                new ErrorGroupSummary(
                    HistoryErrorType.ConnectionRefused,
                    2,
                    request.StartedAt,
                    request.StartedAt.AddMinutes(1)),
            ],
            ErrorCorrelations = new ErrorCorrelationSummary(
                [new CorrelatedErrorGroup(
                    ErrorCorrelationBasis.Operation,
                    operationId,
                    request.StartedAt,
                    request.StartedAt.AddMinutes(1),
                    [HistoryErrorType.ConnectionRefused, HistoryErrorType.ClientCancelled],
                    2)],
                1),
            RuntimeCorrelation = new RuntimeChangeCorrelation(
                RuntimeCorrelationStatus.Sufficient,
                [baselineConfiguration, candidateConfiguration],
                baselineConfiguration,
                candidateConfiguration,
                [new AnalyticsComparison(
                    HistoryMetric.TimeToFirstTokenMilliseconds,
                    aggregate,
                    aggregate with { ArithmeticMean = 40m },
                    20m,
                    true)],
                new AnalyticsComparison(
                    HistoryMetric.ErrorRatePercent,
                    aggregate,
                    aggregate with { ArithmeticMean = 40m },
                    20m,
                    true)),
        };
        string trend = App.HistoryTextPresenter.FormatAnalytics(analytics);
        StringAssert.Contains(trend, "cold=1 | warm=2 | unavailable=3");
        StringAssert.Contains(trend, "mean=20");
        StringAssert.Contains(trend, "P95(nearest-rank)=30");
        StringAssert.Contains(trend, "sufficient");
        StringAssert.Contains(trend, "ConnectionRefused | occurrences=2 | recurring");
        StringAssert.Contains(trend, "Confirmed error correlation by Operation=");
        StringAssert.Contains(trend, "Uncorrelated errors: 1");
        StringAssert.Contains(trend, "baseline=config-a | candidate=config-b");
        StringAssert.Contains(trend, "CONFIRMED REGRESSION");

        TechnicalOperationDetail detail = new(
            new TechnicalOperationRecord(
                operationId, sessionId, request.StartedAt, request.StartedAt.AddSeconds(1),
                ClientKind.Cline, BackendKind.Ollama, Id("model-a"),
                TechnicalOperationStatus.Completed, HistoryErrorType.None),
            [new TechnicalTurnRecord(
                Guid.NewGuid(), operationId, 0, request.RequestId, request.StartedAt,
                TimeSpan.FromMilliseconds(500), ProxyOutcome.Completed, HistoryErrorType.None)
            {
                AvailableToolCount = Count(4),
                InvokedToolCount = Count(1),
            }],
            [new TechnicalToolEventRecord(
                Guid.NewGuid(), operationId, 0, 0, Id("read_file"), request.StartedAt,
                TimeSpan.FromMilliseconds(50), TechnicalToolStatus.Completed, HistoryErrorType.None)],
            [new TechnicalResourceSampleRecord(
                Guid.NewGuid(), operationId, request.StartedAt, Percent(40), Percent(50))]);
        string operation = App.HistoryTextPresenter.FormatOperation(detail);
        StringAssert.Contains(operation, "Turn 0");
        StringAssert.Contains(operation, "tools available=4 [exact]");
        StringAssert.Contains(operation, "tools invoked=1 [exact]");
        StringAssert.Contains(operation, "Tool 0 read_file");
        StringAssert.Contains(operation, "50 [calculated]");
        StringAssert.Contains(operation, "CPU=40 [exact]");

        AnalyticsComparison comparison = new(
            HistoryMetric.TimeToFirstTokenMilliseconds,
            aggregate,
            aggregate with { ArithmeticMean = 40m },
            20m,
            true)
        {
            RecurringErrorFrequency =
            [
                new ErrorFrequencyComparison(
                    HistoryErrorType.ConnectionRefused,
                    2,
                    3,
                    50m,
                    75m,
                    25m),
            ],
        };
        string comparisonText = App.HistoryTextPresenter.FormatComparison(comparison);
        StringAssert.Contains(comparisonText, "CONFIRMED DEGRADATION");
        StringAssert.Contains(comparisonText, "Recurring ConnectionRefused");
        StringAssert.Contains(comparisonText, "delta=+25 p.p.");

        HistoryClearPreview preview = new(
            new HistoryClearScope(allHistory: true),
            1, 2, 3, 4, 5, 6);
        StringAssert.Contains(App.HistoryTextPresenter.FormatClearPreview(preview), "all history");
    }

    private static TechnicalIdentifier Id(string value) =>
        TechnicalIdentifier.FromBackend(value) ?? throw new InvalidOperationException("Invalid test identifier.");

    private static MetricValue Percent(decimal value) =>
        MetricValue.Exact(value, MetricUnit.Percent, MetricSource.Inspector, "history-ui-test-v1");

    private static MetricValue Count(decimal value) =>
        MetricValue.Exact(value, MetricUnit.Count, MetricSource.Inspector, "history-ui-test-v1");
}
