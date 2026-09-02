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
            "Generated request ID, start time, duration, HTTP status and outcome",
            "Process lifetime only"),
    ];

    public const string PersistentDataStatement =
        "Persistent technical datasets: none in the current implementation.";

    public const string ForbiddenContentStatement =
        "Prompt, response, reasoning, tool arguments/results, user code, credentials and raw headers are never retained.";
}
