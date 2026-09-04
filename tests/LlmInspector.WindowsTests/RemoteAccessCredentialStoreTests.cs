using System.Runtime.Versioning;
using System.Security.Cryptography;
using LlmInspector.Application;
using LlmInspector.Resources.Windows;

namespace LlmInspector.WindowsTests;

[TestClass]
[DoNotParallelize]
[SupportedOSPlatform("windows")]
public sealed class RemoteAccessCredentialStoreTests
{
    [TestMethod]
    public void DpapiCurrentUserRoundTripDoesNotReturnPlaintextCiphertext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows DPAPI runtime evidence.");
        }

        WindowsDpapiCurrentUserProtector protector = new();
        byte[] plaintext = Enumerable.Range(1, RemoteAccessManager.BearerTokenBytes)
            .Select(value => (byte)value)
            .ToArray();

        byte[] protectedData = protector.Protect(plaintext);
        byte[] restored = protector.Unprotect(protectedData);

        CollectionAssert.AreNotEqual(plaintext, protectedData);
        CollectionAssert.AreEqual(plaintext, restored);
        CryptographicOperations.ZeroMemory(plaintext);
        CryptographicOperations.ZeroMemory(protectedData);
        CryptographicOperations.ZeroMemory(restored);
    }

    [TestMethod]
    public async Task FileStoreIsDisabledByDefaultAndPersistsOnlyProtectedToken()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows DPAPI runtime evidence.");
        }

        string directory = Path.Combine(Path.GetTempPath(), $"llm-inspector-remote-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "remote-access.json");
        try
        {
            WindowsRemoteAccessCredentialStore store = new(path);
            RemoteAccessStoredConfiguration initial = await store.LoadAsync();
            Assert.IsFalse(initial.Enabled);
            Assert.IsNull(initial.BearerToken);

            byte[] token = Enumerable.Repeat((byte)0xA5, RemoteAccessManager.BearerTokenBytes).ToArray();
            DateTimeOffset updatedAt = new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);
            await store.SaveAsync(new RemoteAccessStoredConfiguration(true, token, updatedAt));

            string document = await File.ReadAllTextAsync(path);
            Assert.IsFalse(document.Contains(Convert.ToBase64String(token), StringComparison.Ordinal));
            Assert.IsFalse(document.Contains("\"bearer_token\":", StringComparison.Ordinal));
            StringAssert.Contains(document, "protected_bearer_token");

            RemoteAccessStoredConfiguration restored = await store.LoadAsync();
            Assert.IsTrue(restored.Enabled);
            Assert.AreEqual(updatedAt, restored.UpdatedAt);
            CollectionAssert.AreEqual(token, restored.BearerToken);
            CryptographicOperations.ZeroMemory(token);
            CryptographicOperations.ZeroMemory(restored.BearerToken!);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
