using System.Text.RegularExpressions;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed partial class CiWorkflowPolicyTests
{
    private static readonly string[] ExpectedPinnedActions =
    [
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
        "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68",
    ];

    [TestMethod]
    public void WorkflowUsesLeastPrivilegeAndTrustedEvents()
    {
        string workflow = ReadWorkflow();

        StringAssert.Contains(workflow, "\n  pull_request:\n");
        StringAssert.Contains(workflow, "\n  push:\n");
        StringAssert.Contains(workflow, "\n      - main\n");
        Assert.DoesNotContain("release/v1.0", workflow, StringComparison.Ordinal);
        StringAssert.Contains(workflow, "\npermissions:\n  contents: read\n");
        StringAssert.Contains(workflow, "\n    runs-on: windows-2025\n");
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", workflow, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EveryExternalActionIsPinnedToTheApprovedCommit()
    {
        string workflow = ReadWorkflow();
        string[] actual = ActionReferenceRegex()
            .Matches(workflow)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] expected = ExpectedPinnedActions.Order(StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }

    private static string ReadWorkflow()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            string workflowPath = Path.Combine(current.FullName, ".github", "workflows", "ci.yml");
            if (File.Exists(workflowPath))
            {
                return File.ReadAllText(workflowPath).Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException("CI workflow was not found from the test output directory.");
    }

    [GeneratedRegex(@"^\s*uses:\s+([^\s#]+)\s+#\s+v\d", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ActionReferenceRegex();
}
