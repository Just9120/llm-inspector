namespace LlmInspector.Application;

public sealed record TechnicalDataCategory(
    string Name,
    string Fields,
    string Retention);

public static class TechnicalDataDisclosure
{
    public static IReadOnlyList<TechnicalDataCategory> CurrentCategories { get; } =
    [
        new(
            "Volatile proxy observation",
            "Generated request ID, current stage/evidence, elapsed time, backend-reported progress, estimated ETA with quality, HTTP status, outcome, configured backend, explicit client attribution, normalized model identity, token usage with quality/provenance, and allowlisted backend-specific metrics",
            "Process lifetime only"),
    ];

    public const string PersistentDataStatement =
        "Persistent technical datasets: none in the current implementation.";

    public const string ForbiddenContentStatement =
        "Prompt, response, reasoning, tool arguments/results, user code, credentials and raw headers are never retained.";
}
