using LlmInspector.Domain;

namespace LlmInspector.Adapters;

internal sealed class LmStudioNativeTelemetryAdapter(string fixtureVersion) : IBackendTelemetryAdapter
{
    public BackendKind Backend => BackendKind.LmStudio;

    public string FixtureVersion { get; } = fixtureVersion;

    public IBackendTelemetrySession CreateSession(string? responseMediaType) =>
        new LmStudioNativeTelemetrySession(
            FixtureVersion,
            responseMediaType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true);

    public BackendResponseTelemetry CreateUnavailable() =>
        BackendResponseTelemetry.Unavailable(Backend, FixtureVersion);
}

internal sealed class LmStudioNativeTelemetrySession : IBackendTelemetrySession
{
    private readonly string _fixtureVersion;
    private readonly bool _isEventStream;
    private StreamingJsonTelemetryExtractor _extractor = new();
    private readonly byte[] _linePrefix = new byte[5];
    private int _linePrefixLength;
    private long _lineLength;
    private bool _prefixDecided;
    private bool _isDataLine;
    private bool _skipOptionalDataSpace;
    private bool _eventHasData;
    private bool _completed;
    private bool _terminalStatsObserved;
    private bool _modelLoadStarted;
    private string? _model;
    private decimal? _inputTokens;
    private decimal? _outputTokens;
    private decimal? _reasoningTokens;
    private decimal? _tokensPerSecond;
    private decimal? _modelLoadSeconds;
    private bool _hasObservedOutputContent;

    public LmStudioNativeTelemetrySession(string fixtureVersion, bool isEventStream)
    {
        _fixtureVersion = fixtureVersion;
        _isEventStream = isEventStream;
    }

    public bool HasObservedOutputContent => _isEventStream && _hasObservedOutputContent;

    public void Observe(ReadOnlySpan<byte> responseBytes)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        if (!_isEventStream)
        {
            _extractor.Observe(responseBytes);
            return;
        }

        foreach (byte value in responseBytes)
        {
            ObserveEventStreamByte(value);
        }
    }

    public BackendResponseTelemetry Complete()
    {
        if (!_completed)
        {
            if (_isEventStream)
            {
                if (_lineLength > 0 && _isDataLine)
                {
                    _extractor.Observe("\n"u8);
                    _eventHasData = true;
                }

                CompleteEvent();
            }
            else
            {
                MergeDocument(_extractor.Complete(), isTerminalDocument: true);
            }

            _completed = true;
        }

        return CreateTelemetry();
    }

    private void ObserveEventStreamByte(byte value)
    {
        if (value == (byte)'\r')
        {
            return;
        }

        if (value == (byte)'\n')
        {
            if (_lineLength == 0)
            {
                CompleteEvent();
            }
            else if (_isDataLine)
            {
                _extractor.Observe("\n"u8);
                _eventHasData = true;
            }

            ResetLine();
            return;
        }

        _lineLength++;
        if (!_prefixDecided)
        {
            if (_linePrefixLength < _linePrefix.Length)
            {
                _linePrefix[_linePrefixLength++] = value;
            }

            if (_linePrefixLength == _linePrefix.Length)
            {
                _prefixDecided = true;
                _isDataLine = _linePrefix.AsSpan().SequenceEqual("data:"u8);
                _skipOptionalDataSpace = _isDataLine;
            }

            return;
        }

        if (!_isDataLine)
        {
            return;
        }

        if (_skipOptionalDataSpace)
        {
            _skipOptionalDataSpace = false;
            if (value == (byte)' ')
            {
                return;
            }
        }

        _extractor.ObserveByte(value);
    }

    private void CompleteEvent()
    {
        if (!_eventHasData)
        {
            return;
        }

        ExtractedTelemetry telemetry = _extractor.Complete();
        MergeDocument(telemetry, telemetry.LmStudioEventType == "chat.end");
        _extractor = new StreamingJsonTelemetryExtractor();
        _eventHasData = false;
    }

    private void MergeDocument(ExtractedTelemetry telemetry, bool isTerminalDocument)
    {
        if (!telemetry.IsValid)
        {
            return;
        }

        _model = telemetry.Model ?? _model;
        if (telemetry.LmStudioEventType == "model_load.start")
        {
            _modelLoadStarted = true;
        }

        if (telemetry.LmStudioEventType == "message.delta" && telemetry.HasRootContent)
        {
            _hasObservedOutputContent = true;
        }

        if (telemetry.LmStudioModelLoadSeconds is decimal loadSeconds)
        {
            _modelLoadSeconds = loadSeconds;
        }

        if (!isTerminalDocument || !telemetry.HasLmStudioStats)
        {
            return;
        }

        _terminalStatsObserved = true;
        _inputTokens = telemetry.LmStudioInputTokens;
        _outputTokens = telemetry.LmStudioOutputTokens;
        _reasoningTokens = telemetry.LmStudioReasoningTokens;
        _tokensPerSecond = telemetry.LmStudioTokensPerSecond;
    }

    private void ResetLine()
    {
        _linePrefixLength = 0;
        _lineLength = 0;
        _prefixDecided = false;
        _isDataLine = false;
        _skipOptionalDataSpace = false;
    }

    private BackendResponseTelemetry CreateTelemetry()
    {
        if (!_terminalStatsObserved)
        {
            return BackendResponseTelemetry.Unavailable(BackendKind.LmStudio, _fixtureVersion);
        }

        MetricValue input = TokenMetric(_inputTokens);
        MetricValue output = TokenMetric(_outputTokens);
        MetricValue total = _inputTokens is decimal inputValue && _outputTokens is decimal outputValue
            ? MetricValue.Calculated(
                inputValue + outputValue,
                MetricUnit.TokenCount,
                MetricSource.Inspector,
                _fixtureVersion,
                "sum-input-output-v1")
            : TokenMetric(null);
        ModelLoadDisposition disposition;
        MetricValue modelLoad;
        if (_modelLoadSeconds is decimal loadSeconds)
        {
            disposition = ModelLoadDisposition.Cold;
            modelLoad = MetricValue.Exact(
                loadSeconds * 1_000m,
                MetricUnit.Milliseconds,
                MetricSource.BackendExtension,
                _fixtureVersion);
        }
        else if (!_modelLoadStarted)
        {
            disposition = ModelLoadDisposition.Warm;
            modelLoad = MetricValue.Exact(
                0,
                MetricUnit.Milliseconds,
                MetricSource.BackendExtension,
                _fixtureVersion);
        }
        else
        {
            disposition = ModelLoadDisposition.Unavailable;
            modelLoad = Unavailable(MetricUnit.Milliseconds);
        }

        return new BackendResponseTelemetry(
            BackendKind.LmStudio,
            TechnicalIdentifier.FromBackend(_model),
            input,
            output,
            total,
            Unavailable(MetricUnit.TokenCount),
            TokenMetric(_reasoningTokens),
            input,
            Unavailable(MetricUnit.TokenCount),
            Unavailable(MetricUnit.TokenCount),
            Unavailable(MetricUnit.TokenCount),
            Unavailable(MetricUnit.TokensPerSecond),
            RateMetric(_tokensPerSecond),
            modelLoad,
            Unavailable(MetricUnit.Milliseconds),
            Array.Empty<BackendMetric>(),
            disposition);
    }

    private MetricValue TokenMetric(decimal? value) => value is decimal exact
        ? MetricValue.Exact(exact, MetricUnit.TokenCount, MetricSource.BackendExtension, _fixtureVersion)
        : Unavailable(MetricUnit.TokenCount);

    private MetricValue RateMetric(decimal? value) => value is decimal exact
        ? MetricValue.Exact(exact, MetricUnit.TokensPerSecond, MetricSource.BackendExtension, _fixtureVersion)
        : Unavailable(MetricUnit.TokensPerSecond);

    private MetricValue Unavailable(MetricUnit unit) =>
        MetricValue.Unavailable(unit, MetricSource.BackendExtension, _fixtureVersion);
}
