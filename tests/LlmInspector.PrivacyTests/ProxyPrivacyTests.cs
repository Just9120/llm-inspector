using System.Text;
using System.Text.Json;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;
using LlmInspector.TestInfrastructure;
using Microsoft.AspNetCore.Http;

namespace LlmInspector.PrivacyTests;

[TestClass]
[DoNotParallelize]
public sealed class ProxyPrivacyTests
{
    [TestMethod]
    public async Task RuntimeCanariesNeverEnterObservationOrCreatedFiles()
    {
        string promptCanary = $"prompt-{Guid.NewGuid():N}";
        string responseCanary = $"response-{Guid.NewGuid():N}";
        string reasoningCanary = $"reasoning-{Guid.NewGuid():N}";
        string toolArgumentsCanary = $"tool-arguments-{Guid.NewGuid():N}";
        string toolResultCanary = $"tool-result-{Guid.NewGuid():N}";
        string credentialCanary = $"credential-{Guid.NewGuid():N}";
        string queryCanary = $"query-{Guid.NewGuid():N}";
        string headerCanary = $"header-{Guid.NewGuid():N}";
        string[] forbiddenCanaries =
        [
            promptCanary,
            responseCanary,
            reasoningCanary,
            toolArgumentsCanary,
            toolResultCanary,
            credentialCanary,
            queryCanary,
            headerCanary,
        ];
        string requestBody =
            $"{{\"messages\":[{{\"content\":\"{promptCanary}\"}},{{\"reasoning\":\"{reasoningCanary}\"}}]," +
            $"\"tool_arguments\":\"{toolArgumentsCanary}\",\"tool_result\":\"{toolResultCanary}\"}}";
        string responseBody = $"{{\"choices\":[{{\"message\":{{\"content\":\"{responseCanary}\"}}}}]}}";
        string inspectionDirectory = Path.Combine(
            Path.GetTempPath(),
            $"llm-inspector-privacy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(inspectionDirectory);

        try
        {
            await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
            {
                using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
                _ = await reader.ReadToEndAsync(context.RequestAborted);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(responseBody, context.RequestAborted);
            });
            FileObservationSink sink = new(inspectionDirectory);
            await using ProxyGateway gateway = ProxyGateway.Create(
                ProxyGatewayOptions.CreateForTesting(0, backend.Address),
                sink,
                BackendTelemetryAdapters.Create(BackendKind.Ollama));
            await gateway.StartAsync();
            using HttpClient client = new()
            {
                BaseAddress = gateway.ListeningAddress,
                Timeout = TimeSpan.FromSeconds(15),
            };
            using HttpRequestMessage request = new(
                HttpMethod.Post,
                $"{ProxyGateway.ChatCompletionsPath}?opaque={queryCanary}")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new("Bearer", credentialCanary);
            request.Headers.TryAddWithoutValidation("X-Opaque-Fixture", headerCanary);

            using HttpResponseMessage response = await client.SendAsync(request);
            string relayedResponse = await response.Content.ReadAsStringAsync();
            ProxyObservation observation = await sink.Recorded.Task.WaitAsync(TimeSpan.FromSeconds(5));

            response.EnsureSuccessStatusCode();
            StringAssert.Contains(relayedResponse, responseCanary, StringComparison.Ordinal);
            Assert.AreEqual(ProxyOutcome.Completed, observation.Outcome);

            string serializedObservation = JsonSerializer.Serialize(observation);
            foreach (string canary in forbiddenCanaries)
            {
                Assert.DoesNotContain(canary, serializedObservation, StringComparison.Ordinal);
            }

            foreach (string file in Directory.EnumerateFiles(inspectionDirectory, "*", SearchOption.AllDirectories))
            {
                string content = await File.ReadAllTextAsync(file);
                foreach (string canary in forbiddenCanaries)
                {
                    Assert.DoesNotContain(canary, content, StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Directory.Delete(inspectionDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void ObservationSchemaHasNoFreeFormContentCarrier()
    {
        string[] allowedProperties =
        [
            nameof(ProxyObservation.BackendTelemetry),
            nameof(ProxyObservation.Client),
            nameof(ProxyObservation.ContextChangeTokens),
            nameof(ProxyObservation.Correlation),
            nameof(ProxyObservation.Duration),
            nameof(ProxyObservation.HttpStatusCode),
            nameof(ProxyObservation.Outcome),
            nameof(ProxyObservation.RequestId),
            nameof(ProxyObservation.StartedAt),
            nameof(ProxyObservation.TimeToFirstToken),
        ];
        string[] actualProperties = typeof(ProxyObservation)
            .GetProperties()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(allowedProperties, actualProperties);
        Assert.IsFalse(typeof(ProxyObservation).GetProperties().Any(property => property.PropertyType == typeof(string)));
    }

    private sealed class FileObservationSink(string directory) : IProxyObservationSink
    {
        public TaskCompletionSource<ProxyObservation> Recorded { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask RecordAsync(
            ProxyObservation observation,
            CancellationToken cancellationToken)
        {
            string path = Path.Combine(directory, $"{observation.RequestId:N}.json");
            string json = JsonSerializer.Serialize(observation);
            await File.WriteAllTextAsync(path, json, cancellationToken);
            Recorded.TrySetResult(observation);
        }
    }
}
