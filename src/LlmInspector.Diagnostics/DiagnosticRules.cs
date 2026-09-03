using System.Globalization;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Diagnostics;

public enum DiagnosticRuleId
{
    LargePrompt,
    SlowGeneration,
    CpuOffload,
    VramPressure,
    ModelLoadingLatency,
    QueueWaitingLatency,
    HighContextUsage,
    BackendUnavailable,
    RequestError,
    ActiveWork,
    ConfirmedStall,
}

public enum DiagnosticConclusionKind
{
    Fact,
    Hypothesis,
    InsufficientData,
}

public enum DiagnosticEvidenceKind
{
    InputTokens,
    GenerationTokensPerSecond,
    ProcessCpuPercent,
    GpuUtilizationPercent,
    GpuVramUsedBytes,
    GpuVramTotalBytes,
    GpuVramUsagePercent,
    ModelLoadMilliseconds,
    ModelLoadDisposition,
    QueueMilliseconds,
    ContextUsageTokens,
    ContextLimitTokens,
    ContextUsagePercent,
    ProxyError,
    ActiveStage,
    ActiveElapsedMilliseconds,
    BackendActivity,
}

public enum BackendActivityState
{
    Working,
    Stalled,
}

public sealed record BackendActivitySignal
{
    public BackendActivitySignal(
        Guid requestId,
        BackendActivityState state,
        DateTimeOffset observedAt,
        string sourceVersion)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A request ID is required for backend activity evidence.", nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(sourceVersion) || sourceVersion.Length > 128)
        {
            throw new ArgumentException("A bounded backend activity source version is required.", nameof(sourceVersion));
        }

        RequestId = requestId;
        State = state;
        ObservedAt = observedAt;
        SourceVersion = sourceVersion;
    }

    public Guid RequestId { get; }

    public BackendActivityState State { get; }

    public DateTimeOffset ObservedAt { get; }

    public string SourceVersion { get; }
}

public sealed record DiagnosticEvidence(
    DiagnosticEvidenceKind Kind,
    MetricValue? Metric = null,
    RequestStageValue? Stage = null,
    ProxyErrorType? Error = null,
    ModelLoadDisposition? ModelLoad = null,
    BackendActivitySignal? Activity = null);

public sealed record DiagnosticConclusion(
    DiagnosticRuleId Rule,
    DiagnosticConclusionKind Kind,
    string RuleVersion,
    string Explanation,
    IReadOnlyList<DiagnosticEvidence> Evidence);

public sealed record DiagnosticInput(
    ProxyObservation? LatestObservation,
    TechnicalResourceSampleRecord? LatestResourceSample,
    LiveRequestCollectionSnapshot? LiveRequests,
    IReadOnlyList<BackendActivitySignal>? BackendActivity = null);

public sealed record DiagnosticRuleOptions
{
    public const string Version1 = "diagnostic-rules-v1";

    public string Version { get; init; } = Version1;

    public decimal LargePromptTokens { get; init; } = 8_192m;

    public decimal SlowGenerationTokensPerSecond { get; init; } = 10m;

    public decimal CpuOffloadProcessCpuPercent { get; init; } = 60m;

    public decimal CpuOffloadGpuUtilizationPercent { get; init; } = 20m;

    public decimal VramPressurePercent { get; init; } = 90m;

    public decimal ModelLoadingMilliseconds { get; init; } = 1_000m;

    public decimal QueueWaitingMilliseconds { get; init; } = 1_000m;

    public decimal HighContextUsagePercent { get; init; } = 90m;

    public decimal StallAssessmentMilliseconds { get; init; } = 30_000m;
}

public sealed class DiagnosticRuleset
{
    private const string RatioDerivationVersion = "diagnostic-ratio-v1";
    private readonly DiagnosticRuleOptions _options;

    public DiagnosticRuleset(DiagnosticRuleOptions? options = null)
    {
        _options = options ?? new DiagnosticRuleOptions();
        Validate(_options);
    }

    public static DiagnosticRuleset Default { get; } = new();

    public IReadOnlyList<DiagnosticConclusion> Evaluate(DiagnosticInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        List<DiagnosticConclusion> conclusions = [];
        if (input.LatestObservation is ProxyObservation observation)
        {
            TechnicalResourceSampleRecord? resource = input.LatestResourceSample?.RequestId == observation.RequestId
                ? input.LatestResourceSample
                : null;
            EvaluateLargePrompt(observation, conclusions);
            EvaluateSlowGeneration(observation, conclusions);
            EvaluateCpuOffload(resource, conclusions);
            EvaluateVramPressure(resource, conclusions);
            EvaluateModelLoading(observation, conclusions);
            EvaluateQueueWaiting(observation, conclusions);
            EvaluateContextUsage(observation, conclusions);
            EvaluateError(observation, conclusions);
        }

        EvaluateLive(input.LiveRequests, input.BackendActivity ?? [], conclusions);
        if (conclusions.Count == 0)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.RequestError,
                DiagnosticConclusionKind.InsufficientData,
                "No completed request or active request is available for diagnostic evaluation."));
        }

        return conclusions;
    }

    private void EvaluateLargePrompt(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        MetricValue metric = observation.BackendTelemetry.PromptTokens;
        if (metric.Value is not decimal value)
        {
            conclusions.Add(Insufficient(DiagnosticRuleId.LargePrompt, "Input-token evidence is unavailable."));
        }
        else if (value >= _options.LargePromptTokens)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.LargePrompt,
                EvidenceKind(metric),
                $"Large prompt rule matched: {Format(value)} input tokens is at least the versioned threshold {Format(_options.LargePromptTokens)}.{EstimateSuffix(metric)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.InputTokens, metric)));
        }
    }

    private void EvaluateSlowGeneration(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        MetricValue metric = observation.BackendTelemetry.CompletionTokensPerSecond;
        if (metric.Value is not decimal value)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.SlowGeneration,
                "Generation-rate evidence is unavailable; slow generation is not asserted."));
        }
        else if (value <= _options.SlowGenerationTokensPerSecond)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.SlowGeneration,
                EvidenceKind(metric),
                $"Slow generation rule matched: {Format(value)} tokens/s is at or below the versioned threshold {Format(_options.SlowGenerationTokensPerSecond)}.{EstimateSuffix(metric)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.GenerationTokensPerSecond, metric)));
        }
    }

    private void EvaluateCpuOffload(
        TechnicalResourceSampleRecord? resource,
        List<DiagnosticConclusion> conclusions)
    {
        MetricValue? processCpu = resource?.ProcessCpuPercent;
        MetricValue? gpu = resource?.GpuUtilizationPercent;
        if (processCpu?.Value is not decimal cpu || gpu?.Value is not decimal gpuLoad)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.CpuOffload,
                "Exact request-correlated process CPU and GPU utilization evidence is required; CPU offload is not asserted."));
        }
        else if (cpu >= _options.CpuOffloadProcessCpuPercent &&
                 gpuLoad <= _options.CpuOffloadGpuUtilizationPercent)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.CpuOffload,
                DiagnosticConclusionKind.Hypothesis,
                "High backend-process CPU with low GPU utilization is consistent with CPU offload, but the counters do not prove backend layer placement.",
                new DiagnosticEvidence(DiagnosticEvidenceKind.ProcessCpuPercent, processCpu),
                new DiagnosticEvidence(DiagnosticEvidenceKind.GpuUtilizationPercent, gpu)));
        }
    }

    private void EvaluateVramPressure(
        TechnicalResourceSampleRecord? resource,
        List<DiagnosticConclusion> conclusions)
    {
        MetricValue? used = resource?.GpuVramUsedBytes;
        MetricValue? total = resource?.GpuVramTotalBytes;
        MetricValue? ratio = Ratio(used, total);
        if (ratio?.Value is not decimal percentage)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.VramPressure,
                "Request-correlated VRAM used and total values are unavailable; VRAM pressure is not asserted."));
        }
        else if (percentage >= _options.VramPressurePercent)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.VramPressure,
                EvidenceKind(ratio),
                $"VRAM pressure rule matched: {Format(percentage)}% is at least the versioned threshold {Format(_options.VramPressurePercent)}%.{EstimateSuffix(ratio)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.GpuVramUsedBytes, used),
                new DiagnosticEvidence(DiagnosticEvidenceKind.GpuVramTotalBytes, total),
                new DiagnosticEvidence(DiagnosticEvidenceKind.GpuVramUsagePercent, ratio)));
        }
    }

    private void EvaluateModelLoading(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        MetricValue metric = observation.BackendTelemetry.ModelLoadTime;
        ModelLoadDisposition disposition = observation.BackendTelemetry.ModelLoadDisposition;
        if (disposition == ModelLoadDisposition.Unavailable)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.ModelLoadingLatency,
                "Model-load classification is unavailable; loading is not asserted as a latency source."));
        }
        else if (disposition == ModelLoadDisposition.Cold && metric.Value is decimal value &&
                 value >= _options.ModelLoadingMilliseconds)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.ModelLoadingLatency,
                EvidenceKind(metric),
                $"Cold model loading contributed {Format(value)} ms, meeting the versioned latency threshold {Format(_options.ModelLoadingMilliseconds)} ms.{EstimateSuffix(metric)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.ModelLoadDisposition, ModelLoad: disposition),
                new DiagnosticEvidence(DiagnosticEvidenceKind.ModelLoadMilliseconds, metric)));
        }
        else if (disposition == ModelLoadDisposition.Cold && metric.Value is null)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.ModelLoadingLatency,
                DiagnosticConclusionKind.Hypothesis,
                "The backend reported a cold load, but load duration is unavailable; its latency contribution cannot be quantified.",
                new DiagnosticEvidence(DiagnosticEvidenceKind.ModelLoadDisposition, ModelLoad: disposition)));
        }
    }

    private void EvaluateQueueWaiting(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        MetricValue metric = observation.BackendTelemetry.QueueTime;
        if (metric.Value is not decimal value)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.QueueWaitingLatency,
                "Backend queue/wait duration is unavailable; queue latency is not asserted."));
        }
        else if (value >= _options.QueueWaitingMilliseconds)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.QueueWaitingLatency,
                EvidenceKind(metric),
                $"Queue/wait time {Format(value)} ms meets the versioned threshold {Format(_options.QueueWaitingMilliseconds)} ms.{EstimateSuffix(metric)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.QueueMilliseconds, metric)));
        }
    }

    private void EvaluateContextUsage(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        MetricValue used = observation.BackendTelemetry.ContextUsageTokens;
        MetricValue limit = observation.BackendTelemetry.ContextLimitTokens;
        MetricValue? ratio = Ratio(used, limit);
        if (ratio?.Value is not decimal percentage)
        {
            conclusions.Add(Insufficient(
                DiagnosticRuleId.HighContextUsage,
                "Context usage and limit are not both available; high context usage is not asserted."));
        }
        else if (percentage >= _options.HighContextUsagePercent)
        {
            conclusions.Add(Conclusion(
                DiagnosticRuleId.HighContextUsage,
                EvidenceKind(ratio),
                $"Context usage {Format(percentage)}% meets the versioned threshold {Format(_options.HighContextUsagePercent)}%.{EstimateSuffix(ratio)}",
                new DiagnosticEvidence(DiagnosticEvidenceKind.ContextUsageTokens, used),
                new DiagnosticEvidence(DiagnosticEvidenceKind.ContextLimitTokens, limit),
                new DiagnosticEvidence(DiagnosticEvidenceKind.ContextUsagePercent, ratio)));
        }
    }

    private void EvaluateError(ProxyObservation observation, List<DiagnosticConclusion> conclusions)
    {
        HistoryErrorType error = HistoryErrorClassifier.From(observation);
        if (error == HistoryErrorType.None)
        {
            return;
        }

        ProxyErrorType proxyError = observation.ErrorType == ProxyErrorType.None
            ? LegacyProxyError(observation.Outcome)
            : observation.ErrorType;
        DiagnosticRuleId rule = error is
            HistoryErrorType.BackendUnavailable or
            HistoryErrorType.ConnectionRefused or
            HistoryErrorType.Timeout or
            HistoryErrorType.BackendCrash
                ? DiagnosticRuleId.BackendUnavailable
                : DiagnosticRuleId.RequestError;
        conclusions.Add(Conclusion(
            rule,
            DiagnosticConclusionKind.Fact,
            ErrorExplanation(error),
            new DiagnosticEvidence(DiagnosticEvidenceKind.ProxyError, Error: proxyError)));
    }

    private void EvaluateLive(
        LiveRequestCollectionSnapshot? live,
        IReadOnlyList<BackendActivitySignal> activities,
        List<DiagnosticConclusion> conclusions)
    {
        if (live is null)
        {
            return;
        }

        foreach (LiveRequestSnapshot request in live.ActiveRequests)
        {
            BackendActivitySignal? activity = activities
                .Where(item => item.RequestId == request.RequestId)
                .OrderByDescending(item => item.ObservedAt)
                .FirstOrDefault();
            if (activity?.State == BackendActivityState.Stalled)
            {
                conclusions.Add(Conclusion(
                    DiagnosticRuleId.ConfirmedStall,
                    DiagnosticConclusionKind.Fact,
                    "A typed backend activity source explicitly reported a stall for this active request.",
                    new DiagnosticEvidence(DiagnosticEvidenceKind.BackendActivity, Activity: activity),
                    new DiagnosticEvidence(DiagnosticEvidenceKind.ActiveStage, Stage: request.Stage)));
                continue;
            }

            string stage = request.Stage.Stage.ToString();
            conclusions.Add(Conclusion(
                DiagnosticRuleId.ActiveWork,
                DiagnosticConclusionKind.Fact,
                activity?.State == BackendActivityState.Working
                    ? $"Backend activity is explicitly reported as working during {stage}."
                    : $"The request remains active in {stage}; this lifecycle fact alone is not a confirmed stall.",
                new DiagnosticEvidence(DiagnosticEvidenceKind.ActiveStage, Stage: request.Stage),
                new DiagnosticEvidence(DiagnosticEvidenceKind.ActiveElapsedMilliseconds, request.Elapsed),
                activity is null ? null : new DiagnosticEvidence(DiagnosticEvidenceKind.BackendActivity, Activity: activity)));

            if (activity is null && request.Elapsed.Value is decimal elapsed &&
                elapsed >= _options.StallAssessmentMilliseconds)
            {
                conclusions.Add(Insufficient(
                    DiagnosticRuleId.ConfirmedStall,
                    $"The request has remained active for {Format(elapsed)} ms, but no typed backend stall signal exists; stall is not asserted."));
            }
        }
    }

    private DiagnosticConclusion Insufficient(DiagnosticRuleId rule, string explanation) =>
        Conclusion(rule, DiagnosticConclusionKind.InsufficientData, explanation);

    private DiagnosticConclusion Conclusion(
        DiagnosticRuleId rule,
        DiagnosticConclusionKind kind,
        string explanation,
        params DiagnosticEvidence?[] evidence) =>
        new(
            rule,
            kind,
            _options.Version,
            explanation,
            evidence.Where(item => item is not null).Select(item => item!).ToArray());

    private MetricValue? Ratio(MetricValue? numerator, MetricValue? denominator)
    {
        if (numerator?.Value is not decimal used || denominator?.Value is not decimal total ||
            total <= 0 || used > total)
        {
            return null;
        }

        decimal ratio = 100m * used / total;
        return numerator.Quality == MetricQuality.Estimated || denominator.Quality == MetricQuality.Estimated
            ? MetricValue.Estimated(
                ratio,
                MetricUnit.Percent,
                MetricSource.Inspector,
                _options.Version,
                RatioDerivationVersion)
            : MetricValue.Calculated(
                ratio,
                MetricUnit.Percent,
                MetricSource.Inspector,
                _options.Version,
                RatioDerivationVersion);
    }

    private static ProxyErrorType LegacyProxyError(ProxyOutcome outcome) => outcome switch
    {
        ProxyOutcome.BackendUnavailable => ProxyErrorType.BackendUnavailable,
        ProxyOutcome.ClientCancelled => ProxyErrorType.ClientCancellation,
        ProxyOutcome.RelayFailed => ProxyErrorType.RelayFailure,
        _ => ProxyErrorType.None,
    };

    private static string ErrorExplanation(HistoryErrorType error) => error switch
    {
        HistoryErrorType.ConnectionRefused => "The backend connection was refused before a response was available.",
        HistoryErrorType.ModelLoading => "The backend returned HTTP 503, classified as model loading/service unavailable by the versioned error rule.",
        HistoryErrorType.HttpApiError => "The backend returned an HTTP/API error; the response body is not retained as diagnostic evidence.",
        HistoryErrorType.Timeout => "The backend connection or response reached a typed timeout condition.",
        HistoryErrorType.ContextOverflow => "The backend returned HTTP 413 or an allowlisted context-overflow error code.",
        HistoryErrorType.ClientCancelled => "The client cancelled the request; this is distinct from a backend or Inspector failure.",
        HistoryErrorType.BackendCrash => "The backend transport ended after response activity; backend crash/disconnect is the error category, while process-crash causality remains unproven without process evidence.",
        HistoryErrorType.BackendUnavailable => "The backend was unavailable, but the transport source did not provide a more specific category.",
        HistoryErrorType.RelayFailed => "The response relay failed without evidence sufficient for a more specific backend category.",
        _ => "A typed technical request error was observed.",
    };

    private static string Format(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static DiagnosticConclusionKind EvidenceKind(MetricValue metric) =>
        metric.Quality == MetricQuality.Estimated
            ? DiagnosticConclusionKind.Hypothesis
            : DiagnosticConclusionKind.Fact;

    private static string EstimateSuffix(MetricValue metric) =>
        metric.Quality == MetricQuality.Estimated
            ? " The threshold match remains a hypothesis because the supporting metric is estimated."
            : string.Empty;

    private static void Validate(DiagnosticRuleOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Version) || options.Version.Length > 128)
        {
            throw new ArgumentException("A bounded diagnostic ruleset version is required.", nameof(options));
        }

        decimal[] positiveThresholds =
        [
            options.LargePromptTokens,
            options.SlowGenerationTokensPerSecond,
            options.CpuOffloadProcessCpuPercent,
            options.VramPressurePercent,
            options.ModelLoadingMilliseconds,
            options.QueueWaitingMilliseconds,
            options.HighContextUsagePercent,
            options.StallAssessmentMilliseconds,
        ];
        if (positiveThresholds.Any(value => value <= 0) ||
            options.CpuOffloadGpuUtilizationPercent is < 0 or > 100 ||
            options.CpuOffloadProcessCpuPercent > 100 ||
            options.VramPressurePercent > 100 ||
            options.HighContextUsagePercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Diagnostic thresholds are outside supported ranges.");
        }
    }
}
