using System.Xml.Linq;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class ArchitectureDependencyTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProductionReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["LlmInspector.Domain"] = [],
            ["LlmInspector.Application"] = ["LlmInspector.Domain"],
            ["LlmInspector.Gateway"] = ["LlmInspector.Application", "LlmInspector.Domain"],
            ["LlmInspector.Adapters"] = ["LlmInspector.Domain"],
            ["LlmInspector.Telemetry"] = ["LlmInspector.Application", "LlmInspector.Domain"],
            ["LlmInspector.Storage.Sqlite"] = ["LlmInspector.Application", "LlmInspector.Domain"],
            ["LlmInspector.Resources.Windows"] = ["LlmInspector.Application", "LlmInspector.Domain"],
            ["LlmInspector.Diagnostics"] = ["LlmInspector.Application", "LlmInspector.Domain"],
            ["LlmInspector.App"] =
            [
                "LlmInspector.Application",
                "LlmInspector.Gateway",
                "LlmInspector.Adapters",
                "LlmInspector.Telemetry",
                "LlmInspector.Storage.Sqlite",
                "LlmInspector.Resources.Windows",
                "LlmInspector.Diagnostics",
            ],
        };

    private static readonly string[] PlannedTestProjects =
    [
        "LlmInspector.UnitTests",
        "LlmInspector.ContractTests",
        "LlmInspector.IntegrationTests",
        "LlmInspector.PrivacyTests",
        "LlmInspector.WindowsTests",
        "LlmInspector.PerformanceTests",
    ];

    [TestMethod]
    public void SolutionContainsExactlyThePlannedProjects()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        XDocument solution = XDocument.Load(Path.Combine(repositoryRoot.FullName, "LlmInspector.slnx"));

        string[] actual = solution
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => path is not null)
            .Select(path => Path.GetFileNameWithoutExtension(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected = AllowedProductionReferences.Keys
            .Concat(PlannedTestProjects)
            .Order(StringComparer.Ordinal)
            .ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void ProductionProjectsFollowTheApprovedDirectDependencyGraph()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();

        foreach ((string projectName, string[] allowedReferences) in AllowedProductionReferences)
        {
            string projectPath = Path.Combine(
                repositoryRoot.FullName,
                "src",
                projectName,
                $"{projectName}.csproj");
            XDocument project = XDocument.Load(projectPath);

            string[] actual = project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(path => path is not null)
                .Select(path => Path.GetFileNameWithoutExtension(path!))
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] expected = allowedReferences.Order(StringComparer.Ordinal).ToArray();

            CollectionAssert.AreEqual(
                expected,
                actual,
                $"Unexpected direct project dependency in {projectName}.");
        }
    }

    [TestMethod]
    public void LockFilesCoverTheSolutionAndWindowsPublishGraphs()
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        string[] solutionProjects = Directory
            .EnumerateFiles(repositoryRoot.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        foreach (string projectPath in solutionProjects)
        {
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            Assert.IsTrue(
                File.Exists(Path.Combine(projectDirectory, "packages.lock.json")),
                $"Normal lock file is missing for {projectPath}.");
        }

        foreach (string projectName in AllowedProductionReferences.Keys)
        {
            string lockPath = Path.Combine(
                repositoryRoot.FullName,
                "src",
                projectName,
                "packages.win-x64.lock.json");
            Assert.IsTrue(File.Exists(lockPath), $"win-x64 lock file is missing for {projectName}.");
        }
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LlmInspector.slnx")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root containing LlmInspector.slnx was not found.");
    }
}
