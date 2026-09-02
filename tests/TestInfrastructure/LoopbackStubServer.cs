using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LlmInspector.TestInfrastructure;

internal sealed class LoopbackStubServer : IAsyncDisposable
{
    private readonly WebApplication _application;

    private LoopbackStubServer(WebApplication application, Uri address)
    {
        _application = application;
        Address = address;
    }

    public Uri Address { get; }

    public static async Task<LoopbackStubServer> StartAsync(RequestDelegate handler)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(LoopbackStubServer).Assembly.GetName().Name,
                EnvironmentName = Environments.Production,
                Args = [],
            });

        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
            serverOptions.Listen(
                IPAddress.Loopback,
                0,
                listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
        });

        WebApplication application = builder.Build();
        application.Run(handler);
        await application.StartAsync().ConfigureAwait(false);

        IServer server = application.Services.GetRequiredService<IServer>();
        string address = server.Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            .Single() ?? throw new InvalidOperationException("Stub server address is unavailable.");

        return new LoopbackStubServer(application, new Uri(address));
    }

    public async ValueTask DisposeAsync()
    {
        await _application.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await _application.DisposeAsync().ConfigureAwait(false);
    }
}
