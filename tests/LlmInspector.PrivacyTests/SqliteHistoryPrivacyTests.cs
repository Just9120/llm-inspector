using System.Net;
using System.Text;
using LlmInspector.Adapters;
using LlmInspector.Application;
using LlmInspector.Domain;
using LlmInspector.Gateway;
using LlmInspector.Storage.Sqlite;
using LlmInspector.TestInfrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace LlmInspector.PrivacyTests;

[TestClass]
[DoNotParallelize]
public sealed class SqliteHistoryPrivacyTests
{
    [TestMethod]
    public async Task RelayedContentCanariesNeverEnterSqliteDatabaseOrWal()
    {
        string[] canaries =
        [
            $"prompt-{Guid.NewGuid():N}",
            $"response-{Guid.NewGuid():N}",
            $"reasoning-{Guid.NewGuid():N}",
            $"tool-arguments-{Guid.NewGuid():N}",
            $"tool-result-{Guid.NewGuid():N}",
            $"credential-{Guid.NewGuid():N}",
            $"raw-header-{Guid.NewGuid():N}",
        ];
        string requestBody =
            $"{{\"messages\":[{{\"content\":\"{canaries[0]}\",\"reasoning\":\"{canaries[2]}\"}}]," +
            $"\"tools\":[{{\"arguments\":\"{canaries[3]}\"}}],\"tool_result\":\"{canaries[4]}\"}}";
        string responseBody =
            $"{{\"choices\":[{{\"message\":{{\"content\":\"{canaries[1]}\",\"reasoning\":\"{canaries[2]}\"," +
            $"\"tool_calls\":[{{\"function\":{{\"arguments\":\"{canaries[3]}\"}}}}]}}}}]," +
            "\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-sqlite-privacy-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(directory, "history.db");
        Directory.CreateDirectory(directory);

        try
        {
            await using LoopbackStubServer backend = await LoopbackStubServer.StartAsync(async context =>
            {
                using StreamReader reader = new(context.Request.Body, Encoding.UTF8);
                _ = await reader.ReadToEndAsync(context.RequestAborted);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(responseBody, context.RequestAborted);
            });
            await using (SqliteTechnicalHistoryStore store = new(databasePath))
            {
                await store.InitializeAsync();
                await using ProxyGateway gateway = ProxyGateway.Create(
                    ProxyGatewayOptions.CreateForTesting(0, backend.Address),
                    store,
                    BackendTelemetryAdapters.Create(BackendKind.Ollama));
                await gateway.StartAsync();
                using HttpClient client = new()
                {
                    BaseAddress = gateway.ListeningAddress,
                    Timeout = TimeSpan.FromSeconds(15),
                };
                using HttpRequestMessage request = new(
                    HttpMethod.Post,
                    ProxyGateway.ChatCompletionsPath)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new("Bearer", canaries[5]);
                request.Headers.TryAddWithoutValidation("X-Privacy-Canary", canaries[6]);
                using HttpResponseMessage response = await client.SendAsync(request);
                string relayed = await response.Content.ReadAsStringAsync();

                Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
                StringAssert.Contains(relayed, canaries[1], StringComparison.Ordinal);
                Assert.HasCount(1, await store.QueryRequestsAsync(new HistoryFilter()));
            }

            SqliteConnection.ClearAllPools();
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                byte[] contents = await File.ReadAllBytesAsync(file);
                foreach (string canary in canaries)
                {
                    Assert.AreEqual(-1, contents.AsSpan().IndexOf(Encoding.UTF8.GetBytes(canary)),
                        $"Content canary was persisted in {Path.GetFileName(file)}.");
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SchemaColumnsMatchTheTechnicalMetadataAllowlist()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-schema-privacy-{Guid.NewGuid():N}");
        string databasePath = Path.Combine(directory, "history.db");
        Directory.CreateDirectory(directory);
        try
        {
            await using (SqliteTechnicalHistoryStore store = new(databasePath))
            {
                await store.InitializeAsync();
            }

            IReadOnlyDictionary<string, string[]> expectedColumns = new Dictionary<string, string[]>
            {
                ["history_settings"] = ["id", "retention"],
                ["sessions"] = ["session_id", "started_at_utc", "ended_at_utc", "client", "backend", "model"],
                ["operations"] = ["operation_id", "session_id", "started_at_utc", "ended_at_utc", "client", "backend", "model", "status", "error_type"],
                ["requests"] = [
                    "request_id", "session_id", "operation_id", "started_at_utc", "http_status_code", "outcome",
                    "error_type", "client", "backend", "model", "correlation_turn_id",
                    "correlation_turn_sequence", "model_load_disposition",
                ],
                ["request_metrics"] = ["request_id", "metric_key", "value", "unit", "quality", "source", "source_version", "derivation_version"],
                ["turns"] = ["turn_id", "operation_id", "sequence", "request_id", "started_at_utc", "duration_ms", "outcome", "error_type"],
                ["tool_events"] = ["tool_event_id", "operation_id", "turn_sequence", "sequence", "tool_name", "started_at_utc", "duration_ms", "status", "error_type"],
                ["resource_samples"] = [
                    "sample_id", "operation_id", "captured_at_utc",
                    "cpu_percent", "cpu_quality", "cpu_source", "cpu_source_version", "cpu_derivation_version",
                    "memory_percent", "memory_quality", "memory_source", "memory_source_version", "memory_derivation_version",
                ],
            };
            await using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            foreach ((string table, string[] expected) in expectedColumns)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"PRAGMA table_info({table});";
                List<string> actual = [];
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    actual.Add(reader.GetString(1));
                }

                CollectionAssert.AreEqual(expected, actual.ToArray(), $"Unexpected persistent fields in {table}.");
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
