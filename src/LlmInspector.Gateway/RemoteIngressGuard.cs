using System.Net;
using LlmInspector.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace LlmInspector.Gateway;

internal static class RemoteIngressGuard
{
    public const string TailscaleLoginHeader = "Tailscale-User-Login";
    public const string TailscaleNameHeader = "Tailscale-User-Name";
    public const string TailscaleProfilePictureHeader = "Tailscale-User-Profile-Pic";
    public const string TailscaleCapabilitiesHeader = "Tailscale-App-Capabilities";

    private static readonly HashSet<string> ProxyIdentityHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        TailscaleLoginHeader,
        TailscaleNameHeader,
        TailscaleProfilePictureHeader,
        TailscaleCapabilitiesHeader,
        "Forwarded",
        "X-Forwarded-For",
        "X-Forwarded-Host",
        "X-Forwarded-Proto",
    };

    public static IReadOnlySet<string> HeadersToStrip => ProxyIdentityHeaders;

    public static async Task AuthorizeAsync(
        HttpContext context,
        IRemoteAccessAuthorizer authorizer,
        RequestDelegate next)
    {
        bool hasProxyHeader = context.Request.Headers.Keys.Any(ProxyIdentityHeaders.Contains);
        bool localHost = IsLocalHost(context.Request.Host.Host);
        if (localHost && !hasProxyHeader)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        bool tailscaleHttpsHost = ProxyGatewayOptions.IsTailscaleDnsName(context.Request.Host.Host);
        bool hasUserIdentity = context.Request.Headers.TryGetValue(
            TailscaleLoginHeader,
            out StringValues loginValues) &&
            loginValues.Count == 1 &&
            !string.IsNullOrWhiteSpace(loginValues[0]);
        if (!tailscaleHttpsHost || !hasUserIdentity)
        {
            await WriteDeniedAsync(context, StatusCodes.Status403Forbidden, "remote_ingress_not_private_serve")
                .ConfigureAwait(false);
            return;
        }

        RemoteAccessSnapshot snapshot = authorizer.Snapshot;
        if (!snapshot.IsAvailable || !snapshot.Enabled)
        {
            await WriteDeniedAsync(context, StatusCodes.Status403Forbidden, "remote_access_disabled")
                .ConfigureAwait(false);
            return;
        }

        if (!TryReadBearerToken(context.Request.Headers.Authorization, out string token) ||
            !authorizer.IsBearerTokenValid(token))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await WriteDeniedAsync(context, StatusCodes.Status401Unauthorized, "remote_authentication_failed")
                .ConfigureAwait(false);
            return;
        }

        context.Items[typeof(RemoteIngressGuard)] = true;
        await next(context).ConfigureAwait(false);
    }

    public static bool IsAuthorizedRemoteRequest(HttpContext context) =>
        context.Items.TryGetValue(typeof(RemoteIngressGuard), out object? value) && value is true;

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string unbracketed = host.Trim('[', ']');
        return IPAddress.TryParse(unbracketed, out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    private static bool TryReadBearerToken(StringValues authorization, out string token)
    {
        token = string.Empty;
        if (authorization.Count != 1)
        {
            return false;
        }

        const string prefix = "Bearer ";
        string value = authorization[0] ?? string.Empty;
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate = value[prefix.Length..];
        if (candidate.Length == 0 || candidate.Any(char.IsWhiteSpace))
        {
            return false;
        }

        token = candidate;
        return true;
    }

    private static async Task WriteDeniedAsync(HttpContext context, int statusCode, string errorType)
    {
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(
            $"{{\"error\":{{\"type\":\"{errorType}\"}}}}",
            CancellationToken.None).ConfigureAwait(false);
    }
}
