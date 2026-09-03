using System.Text;
using System.Text.Json;
using LlmInspector.Domain;

namespace LlmInspector.Adapters;

internal sealed class OpenAiBackendTelemetrySession : IBackendTelemetrySession
{
    private readonly BackendKind _backend;
    private readonly string _fixtureVersion;
    private readonly bool _isEventStream;
    private readonly TelemetryAccumulator _accumulator = new();
    private StreamingJsonTelemetryExtractor _extractor = new();
    private readonly byte[] _linePrefix = new byte[5];
    private int _linePrefixLength;
    private long _lineLength;
    private bool _prefixDecided;
    private bool _isDataLine;
    private bool _skipOptionalDataSpace;
    private bool _eventHasData;
    private bool _completed;

    public OpenAiBackendTelemetrySession(
        BackendKind backend,
        string fixtureVersion,
        bool isEventStream)
    {
        _backend = backend;
        _fixtureVersion = fixtureVersion;
        _isEventStream = isEventStream;
    }

    public bool HasObservedOutputContent =>
        _isEventStream && _accumulator.HasObservedOutputContent;

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
                MergeCurrentDocument();
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

        MergeCurrentDocument();
        _extractor = new StreamingJsonTelemetryExtractor();
        _eventHasData = false;
    }

    private void MergeCurrentDocument()
    {
        ExtractedTelemetry extracted = _extractor.Complete();
        if (extracted.IsValid)
        {
            _accumulator.Merge(extracted);
        }
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
        MetricValue promptTokens = CreateCommonTokenMetric(_accumulator.PromptTokens);
        MetricValue completionTokens = CreateCommonTokenMetric(_accumulator.CompletionTokens);
        MetricValue cachedPromptTokens = CreateCachedPromptTokenMetric();
        MetricValue reasoningTokens = CreateCommonTokenMetric(_accumulator.ReasoningTokens);
        MetricValue totalTokens;
        if (_accumulator.TotalTokens is decimal exactTotal)
        {
            totalTokens = CreateCommonTokenMetric(exactTotal);
        }
        else if (_accumulator.PromptTokens is decimal prompt &&
                 _accumulator.CompletionTokens is decimal completion)
        {
            totalTokens = MetricValue.Calculated(
                prompt + completion,
                MetricUnit.TokenCount,
                MetricSource.Inspector,
                _fixtureVersion,
                "sum-prompt-completion-v1");
        }
        else
        {
            totalTokens = CreateCommonTokenMetric(null);
        }

        IReadOnlyList<BackendMetric> backendMetrics = _backend == BackendKind.LlamaCpp
            ? CreateLlamaCppMetrics()
            : Array.Empty<BackendMetric>();

        return new BackendResponseTelemetry(
            _backend,
            TechnicalIdentifier.FromBackend(_accumulator.Model),
            promptTokens,
            completionTokens,
            totalTokens,
            cachedPromptTokens,
            reasoningTokens,
            promptTokens,
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, _fixtureVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, _fixtureVersion),
            MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.BackendExtension, _fixtureVersion),
            CreateLlamaCppCommonMetric("prompt_per_second", MetricUnit.TokensPerSecond),
            CreateLlamaCppCommonMetric("predicted_per_second", MetricUnit.TokensPerSecond),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.BackendExtension, _fixtureVersion),
            MetricValue.Unavailable(MetricUnit.Milliseconds, MetricSource.BackendExtension, _fixtureVersion),
            backendMetrics);
    }

    private MetricValue CreateCommonTokenMetric(decimal? value) =>
        value is decimal exact && exact == decimal.Truncate(exact)
        ? MetricValue.Exact(exact, MetricUnit.TokenCount, MetricSource.OpenAiUsage, _fixtureVersion)
        : MetricValue.Unavailable(MetricUnit.TokenCount, MetricSource.OpenAiUsage, _fixtureVersion);

    private MetricValue CreateCachedPromptTokenMetric()
    {
        if (_accumulator.CachedPromptTokens is decimal openAiCachedTokens &&
            openAiCachedTokens == decimal.Truncate(openAiCachedTokens))
        {
            return MetricValue.Exact(
                openAiCachedTokens,
                MetricUnit.TokenCount,
                MetricSource.OpenAiUsage,
                _fixtureVersion);
        }

        return CreateLlamaCppCommonMetric("cache_n", MetricUnit.TokenCount);
    }

    private MetricValue CreateLlamaCppCommonMetric(string sourceName, MetricUnit unit)
    {
        if (_backend == BackendKind.LlamaCpp &&
            _accumulator.BackendMetrics.TryGetValue(sourceName, out decimal value) &&
            (unit != MetricUnit.TokenCount || value == decimal.Truncate(value)))
        {
            return MetricValue.Exact(value, unit, MetricSource.BackendExtension, _fixtureVersion);
        }

        return MetricValue.Unavailable(unit, MetricSource.BackendExtension, _fixtureVersion);
    }

    private System.Collections.ObjectModel.ReadOnlyCollection<BackendMetric> CreateLlamaCppMetrics()
    {
        List<BackendMetric> metrics = [];
        AddLlamaMetric(metrics, "cache_n", BackendMetricKey.LlamaCppCachedPromptTokens, MetricUnit.TokenCount);
        AddLlamaMetric(metrics, "prompt_n", BackendMetricKey.LlamaCppEvaluatedPromptTokens, MetricUnit.TokenCount);
        AddLlamaMetric(metrics, "predicted_n", BackendMetricKey.LlamaCppPredictedTokens, MetricUnit.TokenCount);
        AddLlamaMetric(metrics, "prompt_ms", BackendMetricKey.LlamaCppPromptMilliseconds, MetricUnit.Milliseconds);
        AddLlamaMetric(metrics, "predicted_ms", BackendMetricKey.LlamaCppPredictedMilliseconds, MetricUnit.Milliseconds);
        AddLlamaMetric(
            metrics,
            "prompt_per_second",
            BackendMetricKey.LlamaCppPromptTokensPerSecond,
            MetricUnit.TokensPerSecond);
        AddLlamaMetric(
            metrics,
            "predicted_per_second",
            BackendMetricKey.LlamaCppPredictedTokensPerSecond,
            MetricUnit.TokensPerSecond);
        return metrics.AsReadOnly();
    }

    private void AddLlamaMetric(
        List<BackendMetric> destination,
        string sourceName,
        BackendMetricKey key,
        MetricUnit unit)
    {
        if (_accumulator.BackendMetrics.TryGetValue(sourceName, out decimal value) &&
            (unit != MetricUnit.TokenCount || value == decimal.Truncate(value)))
        {
            destination.Add(new BackendMetric(
                key,
                TechnicalIdentifier.FromBackend(sourceName) ??
                    throw new InvalidOperationException("Allowlisted backend metric name is invalid."),
                MetricValue.Exact(value, unit, MetricSource.BackendExtension, _fixtureVersion)));
        }
    }

    private sealed class TelemetryAccumulator
    {
        public string? Model { get; private set; }

        public decimal? PromptTokens { get; private set; }

        public decimal? CompletionTokens { get; private set; }

        public decimal? TotalTokens { get; private set; }

        public decimal? CachedPromptTokens { get; private set; }

        public decimal? ReasoningTokens { get; private set; }

        public Dictionary<string, decimal> BackendMetrics { get; } = new(StringComparer.Ordinal);

        public bool HasObservedOutputContent { get; private set; }

        public void Merge(ExtractedTelemetry telemetry)
        {
            Model = telemetry.Model ?? Model;
            PromptTokens = telemetry.PromptTokens ?? PromptTokens;
            CompletionTokens = telemetry.CompletionTokens ?? CompletionTokens;
            TotalTokens = telemetry.TotalTokens ?? TotalTokens;
            CachedPromptTokens = telemetry.CachedPromptTokens ?? CachedPromptTokens;
            ReasoningTokens = telemetry.ReasoningTokens ?? ReasoningTokens;
            HasObservedOutputContent |= telemetry.HasObservedOutputContent;
            foreach ((string name, decimal value) in telemetry.BackendMetrics)
            {
                BackendMetrics[name] = value;
            }
        }
    }
}

internal sealed class StreamingJsonTelemetryExtractor
{
    private const int MaximumTokenBytes = 256;
    private const int MaximumContainerDepth = 64;
    private const string DiscardedProperty = "#discarded-property#";
    private static readonly string[] AllowedLmStudioEventTypes =
    [
        "chat.start",
        "model_load.start",
        "model_load.progress",
        "model_load.end",
        "prompt_processing.start",
        "prompt_processing.progress",
        "prompt_processing.end",
        "reasoning.start",
        "reasoning.delta",
        "reasoning.end",
        "tool_call.start",
        "tool_call.arguments",
        "tool_call.success",
        "tool_call.failure",
        "message.start",
        "message.delta",
        "message.end",
        "error",
        "chat.end",
    ];

    private readonly Stack<ContainerFrame> _frames = new();
    private readonly List<byte> _token = new(MaximumTokenBytes);
    private readonly Dictionary<string, decimal> _backendMetrics = new(StringComparer.Ordinal);
    private LexerState _lexerState;
    private int _unicodeEscapeDigits;
    private bool _tokenTruncated;
    private bool _rootSeen;
    private bool _rootFinished;
    private bool _invalid;
    private string? _model;
    private decimal? _promptTokens;
    private decimal? _completionTokens;
    private decimal? _totalTokens;
    private decimal? _cachedPromptTokens;
    private decimal? _reasoningTokens;
    private bool _hasObservedOutputContent;
    private string? _lmStudioEventType;
    private decimal? _lmStudioInputTokens;
    private decimal? _lmStudioOutputTokens;
    private decimal? _lmStudioReasoningTokens;
    private decimal? _lmStudioTokensPerSecond;
    private decimal? _lmStudioModelLoadSeconds;
    private bool _hasLmStudioStats;
    private bool _hasRootContent;

    public void Observe(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            ObserveByte(value);
        }
    }

    public void ObserveByte(byte value)
    {
        if (_invalid)
        {
            return;
        }

        switch (_lexerState)
        {
            case LexerState.String:
                ObserveStringByte(value);
                break;
            case LexerState.StringEscape:
                ObserveStringEscapeByte(value);
                break;
            case LexerState.StringUnicodeEscape:
                ObserveUnicodeEscapeByte(value);
                break;
            case LexerState.Number:
                if (IsNumberByte(value))
                {
                    AddTokenByte(value);
                }
                else
                {
                    EmitNumber();
                    ObserveNormalByte(value);
                }

                break;
            case LexerState.Literal:
                if (value is >= (byte)'a' and <= (byte)'z')
                {
                    AddTokenByte(value);
                }
                else
                {
                    EmitLiteral();
                    ObserveNormalByte(value);
                }

                break;
            default:
                ObserveNormalByte(value);
                break;
        }
    }

    public ExtractedTelemetry Complete()
    {
        if (_lexerState == LexerState.Number)
        {
            EmitNumber();
        }
        else if (_lexerState == LexerState.Literal)
        {
            EmitLiteral();
        }

        bool valid = !_invalid &&
            _lexerState == LexerState.Normal &&
            _frames.Count == 0 &&
            _rootSeen &&
            _rootFinished;
        return new ExtractedTelemetry(
            valid,
            _model,
            _promptTokens,
            _completionTokens,
            _totalTokens,
            _cachedPromptTokens,
            _reasoningTokens,
            _hasObservedOutputContent,
            new Dictionary<string, decimal>(_backendMetrics, StringComparer.Ordinal),
            _lmStudioEventType,
            _lmStudioInputTokens,
            _lmStudioOutputTokens,
            _lmStudioReasoningTokens,
            _lmStudioTokensPerSecond,
            _lmStudioModelLoadSeconds,
            _hasLmStudioStats,
            _hasRootContent);
    }

    private void ObserveNormalByte(byte value)
    {
        if (IsWhitespace(value))
        {
            return;
        }

        switch (value)
        {
            case (byte)'{':
                BeginContainer(ContainerKind.Object);
                break;
            case (byte)'[':
                BeginContainer(ContainerKind.Array);
                break;
            case (byte)'}':
                EndContainer(ContainerKind.Object);
                break;
            case (byte)']':
                EndContainer(ContainerKind.Array);
                break;
            case (byte)':':
                ObserveColon();
                break;
            case (byte)',':
                ObserveComma();
                break;
            case (byte)'"':
                BeginToken(LexerState.String);
                break;
            case (byte)'-':
            case >= (byte)'0' and <= (byte)'9':
                BeginToken(LexerState.Number);
                AddTokenByte(value);
                break;
            case (byte)'t':
            case (byte)'f':
            case (byte)'n':
                BeginToken(LexerState.Literal);
                AddTokenByte(value);
                break;
            default:
                _invalid = true;
                break;
        }
    }

    private void ObserveStringByte(byte value)
    {
        if (value == (byte)'"')
        {
            EmitString();
        }
        else if (value == (byte)'\\')
        {
            AddTokenByte(value);
            _lexerState = LexerState.StringEscape;
        }
        else if (value < 0x20)
        {
            _invalid = true;
        }
        else
        {
            AddTokenByte(value);
        }
    }

    private void ObserveStringEscapeByte(byte value)
    {
        AddTokenByte(value);
        if (value == (byte)'u')
        {
            _unicodeEscapeDigits = 0;
            _lexerState = LexerState.StringUnicodeEscape;
        }
        else if (value is (byte)'"' or (byte)'\\' or (byte)'/' or
                 (byte)'b' or (byte)'f' or (byte)'n' or (byte)'r' or (byte)'t')
        {
            _lexerState = LexerState.String;
        }
        else
        {
            _invalid = true;
        }
    }

    private void ObserveUnicodeEscapeByte(byte value)
    {
        if (!IsHexDigit(value))
        {
            _invalid = true;
            return;
        }

        AddTokenByte(value);
        _unicodeEscapeDigits++;
        if (_unicodeEscapeDigits == 4)
        {
            _lexerState = LexerState.String;
        }
    }

    private void BeginContainer(ContainerKind kind)
    {
        string[] path;
        if (_frames.Count >= MaximumContainerDepth)
        {
            _invalid = true;
            return;
        }

        if (_frames.Count == 0)
        {
            if (_rootSeen || kind != ContainerKind.Object)
            {
                _invalid = true;
                return;
            }

            _rootSeen = true;
            path = [];
        }
        else if (!TryBeginValue(out path))
        {
            return;
        }

        if (kind == ContainerKind.Object &&
            (path is ["stats"] or ["result", "stats"]))
        {
            _hasLmStudioStats = true;
        }

        _frames.Push(new ContainerFrame(kind, path));
    }

    private void EndContainer(ContainerKind kind)
    {
        if (_frames.Count == 0 || _frames.Peek().Kind != kind)
        {
            _invalid = true;
            return;
        }

        ContainerFrame frame = _frames.Peek();
        bool complete = frame.CanEnd && (kind == ContainerKind.Object
            ? frame.State is ContainerState.ExpectKeyOrEnd or ContainerState.ExpectCommaOrEnd
            : frame.State is ContainerState.ExpectValueOrEnd or ContainerState.ExpectCommaOrEnd);
        if (!complete)
        {
            _invalid = true;
            return;
        }

        _frames.Pop();
        if (_frames.Count == 0)
        {
            _rootFinished = true;
        }
    }

    private void ObserveColon()
    {
        if (_frames.Count == 0 ||
            _frames.Peek().Kind != ContainerKind.Object ||
            _frames.Peek().State != ContainerState.ExpectColon)
        {
            _invalid = true;
            return;
        }

        _frames.Peek().State = ContainerState.ExpectValue;
    }

    private void ObserveComma()
    {
        if (_frames.Count == 0 || _frames.Peek().State != ContainerState.ExpectCommaOrEnd)
        {
            _invalid = true;
            return;
        }

        ContainerFrame frame = _frames.Peek();
        frame.State = frame.Kind == ContainerKind.Object
            ? ContainerState.ExpectKeyOrEnd
            : ContainerState.ExpectValueOrEnd;
        frame.CurrentProperty = null;
        frame.CanEnd = false;
    }

    private void EmitString()
    {
        _lexerState = LexerState.Normal;

        if (_frames.Count == 0)
        {
            _invalid = true;
            return;
        }

        ContainerFrame frame = _frames.Peek();
        if (frame.Kind == ContainerKind.Object && frame.State == ContainerState.ExpectKeyOrEnd)
        {
            frame.CurrentProperty = DecodeStringToken() ?? DiscardedProperty;
            frame.State = ContainerState.ExpectColon;
            frame.CanEnd = false;
            return;
        }

        if (!TryBeginPrimitiveValue(out string[] path))
        {
            return;
        }

        if (path is ["model"] or ["model_instance_id"] or ["result", "model_instance_id"])
        {
            _model = DecodeStringToken();
        }
        else if (path is ["type"])
        {
            _lmStudioEventType = GetAllowedLmStudioEventType();
        }

        // Non-allowlisted string values are never decoded into managed strings.
        if (_token.Count > 0 && path is ["choices", "delta", "content"])
        {
            _hasObservedOutputContent = true;
        }

        else if (_token.Count > 0 && path is ["content"])
        {
            _hasRootContent = true;
        }
    }

    private void EmitNumber()
    {
        string? token = GetTokenText();
        _lexerState = LexerState.Normal;
        if (!TryBeginPrimitiveValue(out string[] path))
        {
            return;
        }

        if (token is null || !TryParseJsonDecimal(token, out decimal value) || value < 0)
        {
            _invalid = true;
            return;
        }

        if (path is ["usage", "prompt_tokens"])
        {
            if (value != decimal.Truncate(value))
            {
                _invalid = true;
                return;
            }

            _promptTokens = value;
        }
        else if (path is ["usage", "completion_tokens"])
        {
            if (value != decimal.Truncate(value))
            {
                _invalid = true;
                return;
            }

            _completionTokens = value;
        }
        else if (path is ["usage", "total_tokens"])
        {
            if (value != decimal.Truncate(value))
            {
                _invalid = true;
                return;
            }

            _totalTokens = value;
        }
        else if (path is ["usage", "prompt_tokens_details", "cached_tokens"])
        {
            if (value != decimal.Truncate(value))
            {
                _invalid = true;
                return;
            }

            _cachedPromptTokens = value;
        }
        else if (path is ["usage", "completion_tokens_details", "reasoning_tokens"])
        {
            if (value != decimal.Truncate(value))
            {
                _invalid = true;
                return;
            }

            _reasoningTokens = value;
        }
        else if (path is ["stats", "input_tokens"] or ["result", "stats", "input_tokens"])
        {
            SetWholeNumber(value, result => _lmStudioInputTokens = result);
        }
        else if (path is ["stats", "total_output_tokens"] or ["result", "stats", "total_output_tokens"])
        {
            SetWholeNumber(value, result => _lmStudioOutputTokens = result);
        }
        else if (path is ["stats", "reasoning_output_tokens"] or ["result", "stats", "reasoning_output_tokens"])
        {
            SetWholeNumber(value, result => _lmStudioReasoningTokens = result);
        }
        else if (path is ["stats", "tokens_per_second"] or ["result", "stats", "tokens_per_second"])
        {
            _lmStudioTokensPerSecond = value;
        }
        else if (path is ["stats", "model_load_time_seconds"] or
                 ["result", "stats", "model_load_time_seconds"] or
                 ["load_time_seconds"])
        {
            _lmStudioModelLoadSeconds = value;
        }
        else if (path is ["timings", string timingName] && IsAllowedTimingName(timingName))
        {
            _backendMetrics[timingName] = value;
        }
    }

    private void SetWholeNumber(decimal value, Action<decimal> assign)
    {
        if (value != decimal.Truncate(value))
        {
            _invalid = true;
            return;
        }

        assign(value);
    }

    private string? GetAllowedLmStudioEventType()
    {
        if (_tokenTruncated)
        {
            return null;
        }

        foreach (string candidate in AllowedLmStudioEventTypes)
        {
            if (_token.Count != candidate.Length)
            {
                continue;
            }

            bool equal = true;
            for (int index = 0; index < candidate.Length; index++)
            {
                if (_token[index] != (byte)candidate[index])
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
            {
                return candidate;
            }
        }

        return null;
    }

    private void EmitLiteral()
    {
        string? value = GetTokenText();
        _lexerState = LexerState.Normal;
        if (value is not ("true" or "false" or "null"))
        {
            _invalid = true;
            return;
        }

        _ = TryBeginPrimitiveValue(out _);
    }

    private bool TryBeginPrimitiveValue(out string[] path)
    {
        if (_frames.Count == 0)
        {
            _invalid = true;
            path = [];
            return false;
        }

        return TryBeginValue(out path);
    }

    private bool TryBeginValue(out string[] path)
    {
        ContainerFrame parent = _frames.Peek();
        if (parent.Kind == ContainerKind.Object)
        {
            if (parent.State != ContainerState.ExpectValue)
            {
                _invalid = true;
                path = [];
                return false;
            }

            path = parent.CurrentProperty is null
                ? parent.Path
                : [.. parent.Path, parent.CurrentProperty];
        }
        else
        {
            if (parent.State != ContainerState.ExpectValueOrEnd)
            {
                _invalid = true;
                path = [];
                return false;
            }

            path = parent.Path;
        }

        parent.State = ContainerState.ExpectCommaOrEnd;
        parent.CanEnd = true;
        return true;
    }

    private void BeginToken(LexerState state)
    {
        if (_rootFinished)
        {
            _invalid = true;
            return;
        }

        _token.Clear();
        _tokenTruncated = false;
        _lexerState = state;
    }

    private void AddTokenByte(byte value)
    {
        if (_token.Count < MaximumTokenBytes)
        {
            _token.Add(value);
        }
        else
        {
            _tokenTruncated = true;
        }
    }

    private string? DecodeStringToken()
    {
        if (_tokenTruncated)
        {
            return null;
        }

        byte[] quoted = new byte[_token.Count + 2];
        quoted[0] = (byte)'"';
        _token.CopyTo(quoted, 1);
        quoted[^1] = (byte)'"';
        try
        {
            return JsonSerializer.Deserialize<string>(quoted);
        }
        catch (JsonException)
        {
            _invalid = true;
            return null;
        }
    }

    private string? GetTokenText() =>
        _tokenTruncated ? null : Encoding.UTF8.GetString(_token.ToArray());

    private static bool IsAllowedTimingName(string name) => name is
        "cache_n" or
        "prompt_n" or
        "predicted_n" or
        "prompt_ms" or
        "predicted_ms" or
        "prompt_per_second" or
        "predicted_per_second";

    private static bool TryParseJsonDecimal(string token, out decimal value)
    {
        value = default;
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(token));
        return reader.Read() &&
            reader.TokenType == JsonTokenType.Number &&
            reader.BytesConsumed == token.Length &&
            reader.TryGetDecimal(out value);
    }

    private static bool IsWhitespace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    private static bool IsNumberByte(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' or
            (byte)'-' or (byte)'+' or (byte)'.' or (byte)'e' or (byte)'E';

    private static bool IsHexDigit(byte value) =>
        value is >= (byte)'0' and <= (byte)'9' or
            >= (byte)'a' and <= (byte)'f' or
            >= (byte)'A' and <= (byte)'F';

    private enum LexerState
    {
        Normal,
        String,
        StringEscape,
        StringUnicodeEscape,
        Number,
        Literal,
    }

    private enum ContainerKind
    {
        Object,
        Array,
    }

    private enum ContainerState
    {
        ExpectKeyOrEnd,
        ExpectColon,
        ExpectValue,
        ExpectValueOrEnd,
        ExpectCommaOrEnd,
    }

    private sealed class ContainerFrame
    {
        public ContainerFrame(ContainerKind kind, string[] path)
        {
            Kind = kind;
            Path = path;
            State = kind == ContainerKind.Object
                ? ContainerState.ExpectKeyOrEnd
                : ContainerState.ExpectValueOrEnd;
        }

        public ContainerKind Kind { get; }

        public string[] Path { get; }

        public ContainerState State { get; set; }

        public string? CurrentProperty { get; set; }

        public bool CanEnd { get; set; } = true;
    }
}

internal sealed record ExtractedTelemetry(
    bool IsValid,
    string? Model,
    decimal? PromptTokens,
    decimal? CompletionTokens,
    decimal? TotalTokens,
    decimal? CachedPromptTokens,
    decimal? ReasoningTokens,
    bool HasObservedOutputContent,
    IReadOnlyDictionary<string, decimal> BackendMetrics,
    string? LmStudioEventType,
    decimal? LmStudioInputTokens,
    decimal? LmStudioOutputTokens,
    decimal? LmStudioReasoningTokens,
    decimal? LmStudioTokensPerSecond,
    decimal? LmStudioModelLoadSeconds,
    bool HasLmStudioStats,
    bool HasRootContent);
