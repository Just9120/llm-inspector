using System.Text;
using System.Text.Json;
using LlmInspector.Domain;

namespace LlmInspector.Gateway;

internal sealed class BoundedBodyCapture : IDisposable
{
    public const int MaximumBytes = 1_048_576;

    private readonly MemoryStream _buffer = new();
    private bool _overflowed;

    public bool IsComplete { get; private set; }

    public bool IsAvailable => IsComplete && !_overflowed;

    public void Observe(ReadOnlySpan<byte> bytes)
    {
        if (_overflowed || bytes.IsEmpty)
        {
            return;
        }

        if (_buffer.Length + bytes.Length > MaximumBytes)
        {
            _overflowed = true;
            _buffer.SetLength(0);
            return;
        }

        _buffer.Write(bytes);
    }

    public void Complete() => IsComplete = true;

    public byte[] ToArray() => IsAvailable ? _buffer.ToArray() : [];

    public void Dispose() => _buffer.Dispose();
}

internal sealed class CapturingReadStream(
    Stream inner,
    BoundedBodyCapture capture,
    Action<int>? bytesObserved = null) : Stream
{
    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override void Flush() => inner.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read = inner.Read(buffer, offset, count);
        ObserveRead(buffer.AsSpan(offset, read), read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        ObserveRead(buffer[..read], read);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        int read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        ObserveRead(buffer.AsSpan(offset, read), read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        int read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        ObserveRead(buffer.Span[..read], read);
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    private void ObserveRead(ReadOnlySpan<byte> bytes, int read)
    {
        if (read == 0)
        {
            capture.Complete();
        }
        else
        {
            capture.Observe(bytes);
            try
            {
                bytesObserved?.Invoke(read);
            }
            catch (Exception)
            {
                // Resource counters are best-effort and cannot interrupt body relay.
            }
        }
    }
}

internal static class AgentProtocolMetadataExtractor
{
    private const string SourceVersion = "openai-agent-metadata-v1";

    public static AgentTurnTelemetry Extract(
        BoundedBodyCapture request,
        BoundedBodyCapture response,
        string? responseMediaType)
    {
        (MetricValue availableTools, int? toolResults) = ReadRequest(request);
        ResponseMetadata responseMetadata = ReadResponse(response, responseMediaType);
        return new AgentTurnTelemetry(
            availableTools,
            responseMetadata.InvokedTools,
            toolResults,
            responseMetadata.ToolCalls,
            responseMetadata.ToolDetailsComplete,
            responseMetadata.Completion);
    }

    private static (MetricValue AvailableTools, int? ToolResults) ReadRequest(BoundedBodyCapture capture)
    {
        if (!capture.IsAvailable)
        {
            return (UnavailableCount(), null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                capture.ToArray(),
                new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            int available = root.TryGetProperty("tools", out JsonElement tools) &&
                            tools.ValueKind == JsonValueKind.Array
                ? tools.GetArrayLength()
                : 0;
            int results = 0;
            if (root.TryGetProperty("messages", out JsonElement messages) &&
                messages.ValueKind == JsonValueKind.Array)
            {
                JsonElement[] messageList = messages.EnumerateArray().ToArray();
                for (int index = messageList.Length - 1; index >= 0; index--)
                {
                    JsonElement message = messageList[index];
                    if (message.ValueKind == JsonValueKind.Object &&
                        message.TryGetProperty("role", out JsonElement role) &&
                        role.ValueKind == JsonValueKind.String &&
                        role.ValueEquals("tool"u8))
                    {
                        results++;
                        continue;
                    }

                    break;
                }
            }

            return (ExactCount(available), results);
        }
        catch (JsonException)
        {
            return (UnavailableCount(), null);
        }
    }

    private static ResponseMetadata ReadResponse(BoundedBodyCapture capture, string? mediaType)
    {
        if (!capture.IsAvailable)
        {
            return ResponseMetadata.Unavailable;
        }

        ResponseAccumulator accumulator = new();
        try
        {
            byte[] bytes = capture.ToArray();
            if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                ReadSse(bytes, accumulator);
            }
            else
            {
                using JsonDocument document = JsonDocument.Parse(
                    bytes,
                    new JsonDocumentOptions { MaxDepth = 64 });
                accumulator.Observe(document.RootElement);
            }

            return accumulator.Build();
        }
        catch (JsonException)
        {
            return ResponseMetadata.Unavailable;
        }
    }

    private static void ReadSse(ReadOnlySpan<byte> bytes, ResponseAccumulator accumulator)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int relativeEnd = bytes[offset..].IndexOf((byte)'\n');
            int end = relativeEnd < 0 ? bytes.Length : offset + relativeEnd;
            ReadOnlySpan<byte> line = bytes[offset..end].Trim((byte)'\r').Trim((byte)' ');
            offset = relativeEnd < 0 ? bytes.Length : end + 1;
            if (!line.StartsWith("data:"u8))
            {
                continue;
            }

            ReadOnlySpan<byte> data = line[5..].Trim((byte)' ');
            if (data.SequenceEqual("[DONE]"u8) || data.IsEmpty)
            {
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(
                data.ToArray(),
                new JsonDocumentOptions { MaxDepth = 64 });
            accumulator.Observe(document.RootElement);
        }
    }

    private static MetricValue ExactCount(int count) =>
        MetricValue.Exact(count, MetricUnit.Count, MetricSource.Inspector, SourceVersion);

    private static MetricValue UnavailableCount() =>
        MetricValue.Unavailable(MetricUnit.Count, MetricSource.Inspector, SourceVersion);

    private sealed class ResponseAccumulator
    {
        private readonly Dictionary<int, StringBuilder> _toolNames = [];
        private bool _toolDetailsComplete = true;
        private bool _recognized;
        private AgentCompletionDisposition _completion;

        public void Observe(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("choices", out JsonElement choices) ||
                choices.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            _recognized = true;
            foreach (JsonElement choice in choices.EnumerateArray())
            {
                ObserveFinishReason(choice);
                if (choice.TryGetProperty("message", out JsonElement message))
                {
                    ObserveToolCalls(message);
                }

                if (choice.TryGetProperty("delta", out JsonElement delta))
                {
                    ObserveToolCalls(delta);
                }
            }
        }

        public ResponseMetadata Build()
        {
            if (!_recognized)
            {
                return ResponseMetadata.Unavailable;
            }

            List<AgentToolCall> calls = [];
            foreach ((int sequence, StringBuilder nameBuilder) in _toolNames.OrderBy(item => item.Key))
            {
                TechnicalIdentifier? name = TechnicalIdentifier.FromBackend(nameBuilder.ToString());
                if (name is null)
                {
                    _toolDetailsComplete = false;
                    continue;
                }

                calls.Add(new AgentToolCall(sequence, name));
            }

            return new ResponseMetadata(
                ExactCount(_toolNames.Count),
                calls,
                _toolDetailsComplete && calls.Count == _toolNames.Count,
                _completion);
        }

        private void ObserveFinishReason(JsonElement choice)
        {
            if (!choice.TryGetProperty("finish_reason", out JsonElement finishReason) ||
                finishReason.ValueKind != JsonValueKind.String)
            {
                return;
            }

            _completion = finishReason.ValueEquals("tool_calls"u8)
                ? AgentCompletionDisposition.ToolCalls
                : AgentCompletionDisposition.Final;
        }

        private void ObserveToolCalls(JsonElement envelope)
        {
            if (envelope.ValueKind != JsonValueKind.Object ||
                !envelope.TryGetProperty("tool_calls", out JsonElement toolCalls) ||
                toolCalls.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            int fallbackIndex = 0;
            foreach (JsonElement toolCall in toolCalls.EnumerateArray())
            {
                int index = toolCall.TryGetProperty("index", out JsonElement indexElement) &&
                            indexElement.TryGetInt32(out int parsedIndex) && parsedIndex >= 0
                    ? parsedIndex
                    : fallbackIndex;
                fallbackIndex++;
                if (!_toolNames.TryGetValue(index, out StringBuilder? name))
                {
                    name = new StringBuilder();
                    _toolNames.Add(index, name);
                }

                if (toolCall.TryGetProperty("function", out JsonElement function) &&
                    function.ValueKind == JsonValueKind.Object &&
                    function.TryGetProperty("name", out JsonElement nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    name.Append(nameElement.GetString());
                }
            }
        }
    }

    private sealed record ResponseMetadata(
        MetricValue InvokedTools,
        IReadOnlyList<AgentToolCall> ToolCalls,
        bool ToolDetailsComplete,
        AgentCompletionDisposition Completion)
    {
        public static ResponseMetadata Unavailable { get; } = new(
            UnavailableCount(),
            [],
            false,
            AgentCompletionDisposition.Unavailable);
    }
}
