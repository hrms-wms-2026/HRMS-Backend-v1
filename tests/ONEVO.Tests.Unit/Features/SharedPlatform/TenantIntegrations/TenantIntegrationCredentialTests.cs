using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Services;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed class TenantIntegrationCredentialTests
{
    [Fact]
    public async Task Upsert_encrypts_tokens_and_inserts_connected_at()
    {
        var repo = new FakeRepository();
        var handler = new UpsertTenantIntegrationCredentialCommandHandler(repo, new FakeEncryption());

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = Assert.Single(repo.Values);
        Assert.Equal("enc:access-plain", saved.AccessTokenEncrypted);
        Assert.Equal("enc:refresh-plain", saved.RefreshTokenEncrypted);
        Assert.NotEqual("access-plain", saved.AccessTokenEncrypted);
        Assert.NotEqual(default, saved.ConnectedAt);
    }

    [Fact]
    public async Task Upsert_updates_existing_row_and_reconnect_clears_disconnected_at()
    {
        var existing = Entity("disconnected");
        existing.DisconnectedAt = DateTimeOffset.UtcNow;
        var repo = new FakeRepository(existing);
        var handler = new UpsertTenantIntegrationCredentialCommandHandler(repo, new FakeEncryption());

        await handler.Handle(Command(), CancellationToken.None);

        Assert.Single(repo.Values);
        Assert.Null(existing.DisconnectedAt);
        Assert.Equal("connected", existing.Status);
    }

    [Fact]
    public async Task Upsert_with_mixed_case_and_whitespace_stores_normalized_key()
    {
        var repo = new FakeRepository();
        var handler = new UpsertTenantIntegrationCredentialCommandHandler(repo, new FakeEncryption());

        var result = await handler.Handle(Command(" GitHub "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("github", Assert.Single(repo.Values).IntegrationKey);
        Assert.Equal("github", repo.LastIntegrationLookupKey);
    }

    [Fact]
    public async Task Upsert_with_different_key_casing_updates_existing_row_without_duplicate()
    {
        var existing = Entity("connected");
        var repo = new FakeRepository(existing);
        var handler = new UpsertTenantIntegrationCredentialCommandHandler(repo, new FakeEncryption());

        var result = await handler.Handle(Command(" GitHub "), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.Values);
        Assert.Same(existing, repo.Values[0]);
        Assert.Equal("github", repo.LastCredentialLookupKey);
    }

    [Theory]
    [InlineData("user")]
    [InlineData("missing")]
    public async Task Upsert_rejects_user_only_scope_and_unknown_integrations(string caseName)
    {
        var repo = new FakeRepository
        {
            Integration = caseName == "missing" ? null : new IntegrationCatalogEntry
            {
                IntegrationKey = "github", ConnectionScope = "user"
            }
        };
        var handler = new UpsertTenantIntegrationCredentialCommandHandler(repo, new FakeEncryption());

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Empty(repo.Values);
    }

    [Fact]
    public async Task Disconnect_clears_tokens_expiry_and_marks_disconnected()
    {
        var entity = Entity("connected");
        var repo = new FakeRepository(entity);
        var handler = new DisconnectTenantIntegrationCredentialCommandHandler(repo);

        var result = await handler.Handle(
            new DisconnectTenantIntegrationCredentialCommand(entity.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("disconnected", entity.Status);
        Assert.Null(entity.AccessTokenEncrypted);
        Assert.Null(entity.RefreshTokenEncrypted);
        Assert.Null(entity.TokenExpiresAt);
        Assert.NotNull(entity.DisconnectedAt);
    }

    [Theory]
    [InlineData("disconnected")]
    [InlineData("error")]
    [InlineData("expired")]
    public async Task Resolver_returns_null_unless_connected(string status)
    {
        var resolver = new TenantIntegrationCredentialResolver(
            new FakeRepository(Entity(status)), new FakeEncryption());

        var result = await resolver.GetConnectedCredentialAsync(
            TenantId, "github", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Resolver_decrypts_connected_tokens_for_internal_callers()
    {
        var resolver = new TenantIntegrationCredentialResolver(
            new FakeRepository(Entity("connected")), new FakeEncryption());

        var result = await resolver.GetConnectedCredentialAsync(
            TenantId, "github", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("access-plain", result!.AccessToken);
        Assert.Equal("refresh-plain", result.RefreshToken);
    }

    [Fact]
    public async Task Resolver_with_mixed_case_and_whitespace_resolves_normalized_key()
    {
        var repo = new FakeRepository(Entity("connected"));
        var resolver = new TenantIntegrationCredentialResolver(repo, new FakeEncryption());

        var result = await resolver.GetConnectedCredentialAsync(
            TenantId, " GitHub ", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("github", result!.IntegrationKey);
        Assert.Equal("github", repo.LastCredentialLookupKey);
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static UpsertTenantIntegrationCredentialCommand Command(string integrationKey = "github") => new(
        TenantId, integrationKey, "access-plain", "refresh-plain", DateTimeOffset.UtcNow.AddHours(1),
        new[] { "repo" }, "account", "Account", Guid.NewGuid(), "connected");

    private static TenantIntegrationCredential Entity(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, IntegrationKey = "github", Status = status,
        AccessTokenEncrypted = "enc:access-plain", RefreshTokenEncrypted = "enc:refresh-plain",
        TokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1), ConnectedAt = DateTimeOffset.UtcNow,
        ConnectedByUserId = Guid.NewGuid(), ScopesGranted = new[] { "repo" }
    };

    private sealed class FakeEncryption : IEncryptionService
    {
        public string Encrypt(string plainText) => $"enc:{plainText}";
        public string Decrypt(string cipherText) => cipherText[4..];
        public byte[] EncryptBytes(string plainText) => throw new NotSupportedException();
        public string DecryptBytes(byte[] cipherBytes) => throw new NotSupportedException();
    }

    private sealed class FakeRepository : ITenantIntegrationCredentialRepository
    {
        public FakeRepository(params TenantIntegrationCredential[] values) => Values.AddRange(values);
        public List<TenantIntegrationCredential> Values { get; } = new();
        public string? LastIntegrationLookupKey { get; private set; }
        public string? LastCredentialLookupKey { get; private set; }
        public IntegrationCatalogEntry? Integration { get; set; } = new()
        {
            IntegrationKey = "github", ConnectionScope = "tenant"
        };
        public Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(true);
        public Task<IntegrationCatalogEntry?> GetIntegrationAsync(string key, CancellationToken ct)
        {
            LastIntegrationLookupKey = key;
            return Task.FromResult(Integration?.IntegrationKey == key ? Integration : null);
        }
        public Task<IReadOnlyList<TenantIntegrationCredential>> ListByTenantAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<TenantIntegrationCredential>>(Values);
        public Task<TenantIntegrationCredential?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(Values.FirstOrDefault(x => x.Id == id));
        public Task<TenantIntegrationCredential?> GetByTenantAndIntegrationAsync(Guid id, string key, CancellationToken ct)
        {
            LastCredentialLookupKey = key;
            return Task.FromResult(Values.FirstOrDefault(x => x.TenantId == id && x.IntegrationKey == key));
        }
        public Task AddAsync(TenantIntegrationCredential value, CancellationToken ct) { Values.Add(value); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
