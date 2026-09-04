using LlmInspector.Application;

namespace LlmInspector.UnitTests;

[TestClass]
public sealed class RemoteAccessManagerTests
{
    [TestMethod]
    public async Task DefaultsToDisabledAndRequiresExplicitBoundaryConfirmation()
    {
        MemoryStore store = new(new RemoteAccessStoredConfiguration(false, null, null));
        using RemoteAccessManager manager = new(store);

        await manager.InitializeAsync();

        Assert.IsTrue(manager.Snapshot.IsAvailable);
        Assert.IsFalse(manager.Snapshot.Enabled);
        Assert.IsFalse(manager.Snapshot.HasCredential);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.EnableAsync(exactBoundaryConfirmed: false).AsTask());
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task EnableCreatesExactly256BitOneTimeTokenAndPersistsIt()
    {
        MemoryStore store = new(new RemoteAccessStoredConfiguration(false, null, null));
        using RemoteAccessManager manager = new(store);
        await manager.InitializeAsync();

        RemoteAccessChangeResult enabled = await manager.EnableAsync(exactBoundaryConfirmed: true);
        RemoteAccessChangeResult repeated = await manager.EnableAsync(exactBoundaryConfirmed: true);

        Assert.IsTrue(enabled.Snapshot.Enabled);
        Assert.IsNotNull(enabled.OneTimeBearerToken);
        Assert.AreEqual(43, enabled.OneTimeBearerToken.Length);
        Assert.AreEqual(RemoteAccessManager.BearerTokenBytes, Decode(enabled.OneTimeBearerToken).Length);
        Assert.IsTrue(manager.IsBearerTokenValid(enabled.OneTimeBearerToken));
        Assert.IsNull(repeated.OneTimeBearerToken, "An existing credential must not be revealed again.");
        Assert.AreEqual(RemoteAccessManager.BearerTokenBytes, store.Current.BearerToken?.Length);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task RotationInvalidatesPreviousTokenAndDisableRevokesCurrentToken()
    {
        MemoryStore store = new(new RemoteAccessStoredConfiguration(false, null, null));
        using RemoteAccessManager manager = new(store);
        await manager.InitializeAsync();
        string first = (await manager.EnableAsync(true)).OneTimeBearerToken!;

        string rotated = (await manager.RotateAsync(true)).OneTimeBearerToken!;

        Assert.AreNotEqual(first, rotated);
        Assert.IsFalse(manager.IsBearerTokenValid(first));
        Assert.IsTrue(manager.IsBearerTokenValid(rotated));

        await manager.DisableAsync();

        Assert.IsFalse(manager.Snapshot.Enabled);
        Assert.IsFalse(manager.Snapshot.HasCredential);
        Assert.IsFalse(manager.IsBearerTokenValid(rotated));
        Assert.IsNull(store.Current.BearerToken);
    }

    [TestMethod]
    public async Task InvalidStoredCredentialFailsClosed()
    {
        MemoryStore store = new(new RemoteAccessStoredConfiguration(true, [1, 2, 3], DateTimeOffset.UtcNow));
        using RemoteAccessManager manager = new(store);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => manager.InitializeAsync().AsTask());

        Assert.IsFalse(manager.Snapshot.IsAvailable);
        Assert.IsFalse(manager.IsBearerTokenValid("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [TestMethod]
    public async Task FailedPersistenceDoesNotEnableRemoteAccess()
    {
        MemoryStore store = new(new RemoteAccessStoredConfiguration(false, null, null))
        {
            FailSaves = true,
        };
        using RemoteAccessManager manager = new(store);
        await manager.InitializeAsync();

        await Assert.ThrowsExactlyAsync<IOException>(() => manager.EnableAsync(true).AsTask());

        Assert.IsFalse(manager.Snapshot.Enabled);
        Assert.IsFalse(manager.Snapshot.HasCredential);
    }

    private static byte[] Decode(string token) =>
        Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "=");

    private sealed class MemoryStore(RemoteAccessStoredConfiguration initial) : IRemoteAccessCredentialStore
    {
        public RemoteAccessStoredConfiguration Current { get; private set; } = Clone(initial);

        public bool FailSaves { get; init; }

        public int SaveCount { get; private set; }

        public ValueTask<RemoteAccessStoredConfiguration> LoadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Clone(Current));

        public ValueTask SaveAsync(
            RemoteAccessStoredConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            if (FailSaves)
            {
                throw new IOException("Fixture persistence failure.");
            }

            Current = Clone(configuration);
            SaveCount++;
            return ValueTask.CompletedTask;
        }

        private static RemoteAccessStoredConfiguration Clone(RemoteAccessStoredConfiguration value) =>
            value with { BearerToken = value.BearerToken?.ToArray() };
    }
}
