using System.Text.RegularExpressions;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed partial class ReleaseWorkflowPolicyTests
{
    private static readonly string[] ExpectedPinnedActions =
    [
        "actions/attest-build-provenance@4d101475d8b20a2381f78447822ac1eab6504dd8",
        "actions/attest-sbom@c604332985a26aa8cf1bdc465b92731239ec6b9e",
        "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
        "actions/download-artifact@3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c",
        "actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68",
        "actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a",
    ];

    [TestMethod]
    public void ReleaseWorkflowUsesOnlyTrustedTagsAndSplitLeastPrivilegeJobs()
    {
        string workflow = ReadRepositoryFile(".github", "workflows", "release.yml");
        int publishStart = workflow.IndexOf("\n  publish:\n", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, publishStart);
        string publishJob = workflow[publishStart..];

        StringAssert.Contains(workflow, "\n    tags:\n      - \"v*\"\n");
        StringAssert.Contains(workflow, "\npermissions:\n  contents: read\n");
        StringAssert.Contains(workflow, "\n    name: build-portable-win-x64\n");
        StringAssert.Contains(workflow, "\n    name: attest-and-publish-github-release\n");
        StringAssert.Contains(workflow, "\n      contents: write\n      id-token: write\n      attestations: write\n");
        StringAssert.Contains(workflow, "git merge-base --is-ancestor");
        StringAssert.Contains(workflow, "subject-checksums: release-payload/assets/SHA256SUMS.txt");
        StringAssert.Contains(workflow, "--verify-tag");
        StringAssert.Contains(workflow, "cancel-in-progress: false");
        Assert.DoesNotContain("pull_request", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_dispatch", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("workflow_run", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("environment:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/checkout@", publishJob, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet ", publishJob, StringComparison.Ordinal);
    }

    [TestMethod]
    public void EveryReleaseActionIsPinnedToTheReviewedCommit()
    {
        string workflow = ReadRepositoryFile(".github", "workflows", "release.yml");
        string[] actual = ActionReferenceRegex()
            .Matches(workflow)
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(ExpectedPinnedActions, actual);
    }

    [TestMethod]
    public void PortablePublishAndPayloadContractsAreExplicit()
    {
        string project = ReadRepositoryFile("src", "LlmInspector.App", "LlmInspector.App.csproj");
        string generator = ReadRepositoryFile("eng", "release", "New-ReleasePayload.ps1");
        string verifier = ReadRepositoryFile("eng", "release", "Test-ReleasePayload.ps1");

        StringAssert.Contains(project, "<PublishSingleFile>true</PublishSingleFile>");
        StringAssert.Contains(project, "<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>");
        StringAssert.Contains(generator, "SPDX-2.3");
        StringAssert.Contains(generator, "portable-release-v1");
        StringAssert.Contains(generator, "SmartScreen");
        StringAssert.Contains(verifier, "Checksum mismatch");
        StringAssert.Contains(verifier, "signed -ne $false");
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {string.Join('/', segments)}");
    }

    [GeneratedRegex(@"^\s*uses:\s+([^\s#]+)\s+#\s+v\d", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ActionReferenceRegex();
}
