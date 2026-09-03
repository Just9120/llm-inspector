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
            "Generated request ID, pseudonymous operation/session/turn correlation IDs and sequence when explicitly supplied, current stage/evidence, elapsed time, backend-reported progress, estimated ETA with quality, HTTP status, outcome, configured backend, explicit client attribution, normalized model identity, available/invoked tool counts, normalized tool names, token/context/timing metrics with quality/provenance, model-load classification, and allowlisted backend-specific metrics",
            "Process lifetime only"),
        new(
            "Local SQLite technical history",
            "Pseudonymous request/session/turn/operation IDs, timestamps, durations, status/error classification, configured client/backend, normalized model/tool/GPU identifiers, available/invoked tool counts, tool duration quality/provenance, request/stage-correlated host GPU/CPU/RAM resource samples, exact related-process identity when provable, process CPU/RAM/disk counters, gateway traffic byte counters, model-load classification, and allowlisted numeric metrics with quality/provenance",
            "User-selected: 7 days, 30 days (default), 90 days, or indefinite; explicit clear is available"),
    ];

    public const string PersistentDataStatement =
        "Persistent technical history is stored locally per user in %LOCALAPPDATA%\\LLM Inspector\\data\\inspector.db; " +
        "the default retention is 30 days and the selectable options are 7 days, 30 days, 90 days, or indefinite.";

    public const string ForbiddenContentStatement =
        "Prompt, response, reasoning, tool arguments/results, user code, credentials and raw headers are never retained.";
}
