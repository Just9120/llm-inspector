using System.Collections.Concurrent;
using LlmInspector.Application;
using LlmInspector.Domain;

namespace LlmInspector.Telemetry;

public sealed class LiveRequestTracker : ILiveRequestStateSink, ILiveRequestSnapshotSource
{
    private const string GatewayStageSourceVersion = "gateway-lifecycle-v1";
    private const string ElapsedSourceVersion = "monotonic-clock-v1";
    private const string ElapsedDerivationVersion = "monotonic-elapsed-v1";
    private const string NoProgressSourceVersion = "no-backend-progress-v1";
    private const string EtaSourceVersion = "live-eta-v1";
    private const string EtaDerivationVersion = "linear-backend-progress-v1";
    private const int MaximumEstimatorSamples = 4;
    private const int MinimumEstimatorSamples = 3;
    private const decimal MinimumEstimatorProgressSpan = 5m;

    private readonly ConcurrentDictionary<Guid, TrackedRequest> _active = new();
    private readonly TimeProvider _timeProvider;
    private LiveRequestSnapshot? _latestTerminal;

    public LiveRequestTracker(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int ActiveCount => _active.Count;

    public void RequestStarted(Guid requestId, DateTimeOffset startedAt, ClientKind client)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException("A generated request ID is required.", nameof(requestId));
        }

        TrackedRequest request = new(
            requestId,
            client,
            startedAt,
            _timeProvider.GetTimestamp(),
            RequestStageValue.ProtocolObserved(RequestStage.QueueWaiting, GatewayStageSourceVersion),
            null,
            []);
        if (!_active.TryAdd(requestId, request))
        {
            throw new InvalidOperationException("The request is already active.");
        }
    }

    public void StageChanged(Guid requestId, RequestStageValue stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (stage.IsTerminal)
        {
            throw new ArgumentException(
                "Terminal stages are derived from the proxy outcome.",
                nameof(stage));
        }

        UpdateActive(requestId, current => current with { Stage = stage });
    }

    public void BackendProgressChanged(Guid requestId, BackendProgressSignal progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        long capturedTimestamp = _timeProvider.GetTimestamp();
        UpdateActive(requestId, current =>
        {
            ProgressSample sample = new(progress, capturedTimestamp);
            ProgressSample[] samples;
            if (current.Progress is null ||
                !string.Equals(
                    current.Progress.SourceVersion,
                    progress.SourceVersion,
                    StringComparison.Ordinal) ||
                progress.Percentage <= current.Progress.Percentage)
            {
                samples = [sample];
            }
            else
            {
                samples = [.. current.ProgressSamples, sample];
                if (samples.Length > MaximumEstimatorSamples)
                {
                    samples = samples[^MaximumEstimatorSamples..];
                }
            }

            return current with
            {
                Progress = progress,
                ProgressSamples = samples,
            };
        });
    }

    public void RequestFinished(Guid requestId, ProxyOutcome outcome)
    {
        if (!_active.TryRemove(requestId, out TrackedRequest? request))
        {
            return;
        }

        RequestStage terminalStage = outcome switch
        {
            ProxyOutcome.Completed => RequestStage.Completed,
            ProxyOutcome.ClientCancelled => RequestStage.Cancelled,
            ProxyOutcome.BackendUnavailable or ProxyOutcome.RelayFailed => RequestStage.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
        };
        TrackedRequest terminal = request with
        {
            Stage = RequestStageValue.ProtocolObserved(terminalStage, GatewayStageSourceVersion),
        };
        Volatile.Write(ref _latestTerminal, CreateSnapshot(terminal, _timeProvider.GetTimestamp(), terminal: true));
    }

    public LiveRequestCollectionSnapshot GetSnapshot()
    {
        long capturedTimestamp = _timeProvider.GetTimestamp();
        LiveRequestSnapshot[] active = _active.Values
            .OrderBy(item => item.StartedAt)
            .ThenBy(item => item.RequestId)
            .Select(item => CreateSnapshot(item, capturedTimestamp, terminal: false))
            .ToArray();
        return new LiveRequestCollectionSnapshot(active, Volatile.Read(ref _latestTerminal));
    }

    private void UpdateActive(Guid requestId, Func<TrackedRequest, TrackedRequest> update)
    {
        while (_active.TryGetValue(requestId, out TrackedRequest? current))
        {
            TrackedRequest updated = update(current);
            if (_active.TryUpdate(requestId, updated, current))
            {
                return;
            }
        }
    }

    private LiveRequestSnapshot CreateSnapshot(
        TrackedRequest request,
        long capturedTimestamp,
        bool terminal)
    {
        TimeSpan elapsed = _timeProvider.GetElapsedTime(request.StartedTimestamp, capturedTimestamp);
        decimal elapsedMilliseconds = Math.Max(0m, (decimal)elapsed.TotalMilliseconds);
        MetricValue progress = request.Progress?.ToMetric() ?? MetricValue.Unavailable(
            MetricUnit.Percent,
            MetricSource.BackendExtension,
            NoProgressSourceVersion);
        MetricValue eta = terminal
            ? CreateUnavailableEta()
            : CreateEta(request.ProgressSamples);

        return new LiveRequestSnapshot(
            request.RequestId,
            request.Client,
            request.Stage,
            request.StartedAt,
            MetricValue.Calculated(
                elapsedMilliseconds,
                MetricUnit.Milliseconds,
                MetricSource.Inspector,
                ElapsedSourceVersion,
                ElapsedDerivationVersion),
            progress,
            eta);
    }

    private MetricValue CreateEta(IReadOnlyList<ProgressSample> samples)
    {
        if (samples.Count < MinimumEstimatorSamples)
        {
            return CreateUnavailableEta();
        }

        ProgressSample first = samples[0];
        ProgressSample last = samples[^1];
        decimal progressSpan = last.Signal.Percentage - first.Signal.Percentage;
        TimeSpan observed = _timeProvider.GetElapsedTime(first.Timestamp, last.Timestamp);
        if (progressSpan < MinimumEstimatorProgressSpan ||
            last.Signal.Percentage >= 100m ||
            observed <= TimeSpan.Zero)
        {
            return CreateUnavailableEta();
        }

        decimal remainingMilliseconds =
            (decimal)observed.TotalMilliseconds * (100m - last.Signal.Percentage) / progressSpan;
        return MetricValue.Estimated(
            remainingMilliseconds,
            MetricUnit.Milliseconds,
            MetricSource.Inspector,
            EtaSourceVersion,
            EtaDerivationVersion);
    }

    private static MetricValue CreateUnavailableEta() =>
        MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.Inspector, EtaSourceVersion);

    private sealed record TrackedRequest(
        Guid RequestId,
        ClientKind Client,
        DateTimeOffset StartedAt,
        long StartedTimestamp,
        RequestStageValue Stage,
        BackendProgressSignal? Progress,
        ProgressSample[] ProgressSamples);

    private sealed record ProgressSample(BackendProgressSignal Signal, long Timestamp);
}
