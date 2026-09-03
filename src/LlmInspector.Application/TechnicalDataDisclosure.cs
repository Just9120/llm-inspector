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
            "Generated request ID, pseudonymous operation/session/turn correlation IDs and sequence when explicitly supplied, current stage/evidence, elapsed time, backend-reported progress, estimated ETA with quality, HTTP status, typed error category, outcome, configured backend, explicit client attribution, normalized model identity, available/invoked tool counts, normalized tool names, token/context/timing metrics with quality/provenance, model-load classification, and allowlisted backend-specific metrics",
            "Process lifetime only"),
        new(
            "Local SQLite technical history",
            "Pseudonymous request/session/turn/operation IDs, timestamps, durations, status/error classification, configured client/backend, normalized model/tool/GPU identifiers, available/invoked tool counts, tool duration quality/provenance, request/stage-correlated host GPU/CPU/RAM resource samples, exact related-process identity when provable, process CPU/RAM/disk counters, gateway traffic byte counters, model-load classification, and allowlisted numeric metrics with quality/provenance",
            "User-selected: 7 days, 30 days (default), 90 days, or indefinite; explicit clear is available"),
        new(
            "Local background preferences",
            "Schema version, Windows autostart selection, four independent notification selections, and silent-mode selection",
            "Until the user changes the settings or deletes the local settings file"),
        new(
            "User-created diagnostic snapshot",
            "Versioned selection, availability-marked environment facts, pseudonymous request/operation IDs, normalized model/backend/client identities, typed errors, allowlisted runtime metrics, and interval-correlated system metrics",
            "User-selected local JSON file; retained until the user deletes it"),
    ];

    public const string PersistentDataStatement =
        "Persistent technical history is stored locally per user in %LOCALAPPDATA%\\LLM Inspector\\data\\inspector.db; " +
        "the default retention is 30 days and the selectable options are 7 days, 30 days, 90 days, or indefinite. " +
        "Background preferences are stored separately in %LOCALAPPDATA%\\LLM Inspector\\settings.json until changed or deleted. " +
        "A diagnostic snapshot is stored only at the local path explicitly selected by the user and is not uploaded.";

    public const string ForbiddenContentStatement =
        "Prompt, response, reasoning, tool arguments/results, user code, credentials and raw headers are never retained.";
}
