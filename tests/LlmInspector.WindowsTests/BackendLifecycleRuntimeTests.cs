using System.Net;
using System.Net.Sockets;
using LlmInspector.Application;
using LlmInspector.Resources.Windows;

namespace LlmInspector.WindowsTests;

[TestClass]
public sealed class BackendLifecycleRuntimeTests
{
    [TestMethod]
    public async Task ManualDiscoveryAcceptsOnlyAnExistingAbsoluteExe()
    {
        using WindowsBackendLifecycleRuntime runtime = new();
        string processPath = Environment.ProcessPath!;

        string? exact = await runtime.ResolveExecutableAsync([], processPath, CancellationToken.None);
        string? relative = await runtime.ResolveExecutableAsync([], Path.GetFileName(processPath), CancellationToken.None);
        string? missing = await runtime.ResolveExecutableAsync([], @"C:\missing\runtime.exe", CancellationToken.None);

        Assert.AreEqual(Path.GetFullPath(processPath), exact);
        Assert.IsNull(relative);
        Assert.IsNull(missing);
    }

    [TestMethod]
    public async Task ListenerResolutionReturnsExactPidStartTimeAndExecutable()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using WindowsBackendLifecycleRuntime runtime = new();

        BackendProcessIdentity? identity = await runtime.ResolveEndpointOwnerAsync(
            new Uri($"http://127.0.0.1:{port}/"),
            CancellationToken.None);

        Assert.IsNotNull(identity);
        Assert.AreEqual(Environment.ProcessId, identity.ProcessId);
        Assert.IsTrue(Path.IsPathFullyQualified(identity.ExecutablePath));
        Assert.IsTrue(identity.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(identity.StartedAt <= DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public void ProcessRuntimeUsesNoShellAndRechecksIdentityBeforeForceStop()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot().FullName,
            "src",
            "LlmInspector.Resources.Windows",
            "WindowsBackendLifecycleRuntime.cs"));

        StringAssert.Contains(source, "UseShellExecute = false");
        StringAssert.Contains(source, "start.ArgumentList.Add(argument)");
        StringAssert.Contains(source, "if (!IsSameProcessAlive(identity))");
        StringAssert.Contains(source, "GetExactProcess(identity)");
        Assert.IsFalse(source.Contains("cmd.exe", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(source.Contains("powershell", StringComparison.OrdinalIgnoreCase));
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
