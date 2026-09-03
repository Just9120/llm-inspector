using System.Net.Sockets;
using System.Text.Json;
using LlmInspector.Domain;
using Microsoft.AspNetCore.Http;

namespace LlmInspector.Gateway;

internal static class ProxyErrorClassifier
{
    public static ProxyErrorType FromResponse(int statusCode, BoundedBodyCapture response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (statusCode < 400)
        {
            return ProxyErrorType.None;
        }

        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            return ProxyErrorType.ModelLoading;
        }

        if (statusCode is StatusCodes.Status408RequestTimeout or StatusCodes.Status504GatewayTimeout)
        {
            return ProxyErrorType.Timeout;
        }

        if (statusCode == StatusCodes.Status413PayloadTooLarge || HasContextOverflowCode(response))
        {
            return ProxyErrorType.ContextOverflow;
        }

        return ProxyErrorType.HttpApiError;
    }

    public static ProxyErrorType FromTransport(Exception exception, bool responseStarted)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SocketException? socket = FindSocketException(exception);
        if (socket?.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return ProxyErrorType.ConnectionRefused;
        }

        if (socket?.SocketErrorCode == SocketError.TimedOut || exception is TimeoutException)
        {
            return ProxyErrorType.Timeout;
        }

        if (responseStarted || socket?.SocketErrorCode is
                SocketError.ConnectionAborted or
                SocketError.ConnectionReset or
                SocketError.NetworkReset or
                SocketError.Shutdown)
        {
            return ProxyErrorType.BackendCrash;
        }

        if (exception is IOException)
        {
            return ProxyErrorType.RelayFailure;
        }

        return ProxyErrorType.BackendUnavailable;
    }

    private static bool HasContextOverflowCode(BoundedBodyCapture response)
    {
        if (!response.IsAvailable)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(
                response.ToArray(),
                new JsonDocumentOptions { MaxDepth = 16 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("error", out JsonElement error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            return IsContextOverflowValue(error, "code") || IsContextOverflowValue(error, "type");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsContextOverflowValue(JsonElement error, string propertyName)
    {
        if (!error.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return value.ValueEquals("context_length_exceeded"u8) ||
               value.ValueEquals("context_window_exceeded"u8) ||
               value.ValueEquals("context_overflow"u8);
    }

    private static SocketException? FindSocketException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket;
            }
        }

        return null;
    }
}
