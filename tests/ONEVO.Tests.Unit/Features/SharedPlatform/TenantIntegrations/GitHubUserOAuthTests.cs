using MediatR;
using NSubstitute;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.RefreshOwnGitHubConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.SetGitHubTenantApproval;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.DTOs.Responses;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Helpers;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Queries.GetGitHubUserIntegrationStatus;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.IntegrationCatalog.Entities;
using ONEVO.Domain.Features.SharedPlatform.TenantIntegrations.Entities;

namespace ONEVO.Tests.Unit.Features.SharedPlatform.TenantIntegrations;

public sealed class GitHubUserOAuthTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Start_builds_safe_authorization_url_for_authenticated_user()
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        protector.Protect(Arg.Any<GitHubOAuthState>()).Returns("protected-state");
        var handler = new StartGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(),
            protector);

        var result = await handler.Handle(
            new StartGitHubUserOAuthCommand(
                "/settings/integrations",
                "https://tenant.test/api/v1/integrations/github/connect/callback"),
            default);

        Assert.True(result.IsSuccess, result.Error);
        var url = result.Value!.AuthorizationUrl;
        Assert.Contains("client_id=client-id", url);
        Assert.Contains("redirect_uri=", url);
        Assert.Contains("scope=repo", url);
        Assert.Contains("state=protected-state", url);
        Assert.DoesNotContain("client_secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Start_fails_when_catalog_entry_is_inactive()
    {
        var handler = new StartGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(isActive: false),
            Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(
            new StartGitHubUserOAuthCommand(null, "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Fact]
    public async Task Start_fails_when_no_linked_module_is_entitled()
    {
        var handler = new StartGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(activeModules: []),
            Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(
            new StartGitHubUserOAuthCommand(null, "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Start_fails_when_catalog_scope_is_tenant_only()
    {
        var handler = new StartGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(connectionScope: "tenant"),
            Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(
            new StartGitHubUserOAuthCommand(null, "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("disconnected")]
    [InlineData("disabled")]
    public async Task Start_fails_without_connected_tenant_approval(string? approvalStatus)
    {
        var handler = new StartGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(tenantApprovalStatus: approvalStatus),
            Substitute.For<IOAuthStateProtector>());

        var result = await handler.Handle(
            new StartGitHubUserOAuthCommand(null, "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
    }

    [Theory]
    [InlineData(null, "state", "OAuth code is required.")]
    [InlineData("code", null, "OAuth state is required.")]
    [InlineData("code", "invalid", "OAuth state is invalid.")]
    public async Task Callback_rejects_missing_or_invalid_inputs(
        string? code,
        string? state,
        string expectedError)
    {
        var protector = Substitute.For<IOAuthStateProtector>();
        GitHubOAuthState? ignored;
        protector.TryUnprotect("invalid", out ignored).Returns(false);
        var handler = CompleteHandler(protector);

        var result = await handler.Handle(
            new CompleteGitHubUserOAuthCommand(
                code,
                state,
                "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
        Assert.Equal(expectedError, result.Error);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("tenant")]
    [InlineData("user")]
    [InlineData("provider")]
    [InlineData("session")]
    public async Task Callback_rejects_state_binding_failures(string mismatch)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new GitHubOAuthState(
            "nonce",
            mismatch == "tenant" ? Guid.NewGuid() : TenantId,
            mismatch == "user" ? Guid.NewGuid() : UserId,
            "github",
            mismatch == "provider" ? "other" : "github",
            "/settings",
            now.AddMinutes(-1),
            mismatch == "expired" ? now.AddSeconds(-1) : now.AddMinutes(5),
            mismatch == "session" ? "other" : "session-hash");
        var protector = ProtectorFor(payload);
        var handler = CompleteHandler(protector);

        var result = await handler.Handle(
            new CompleteGitHubUserOAuthCommand(
                "code",
                "state",
                "https://tenant.test/callback"),
            default);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Callback_sends_user_scoped_connection_and_not_tenant_credential_command()
    {
        var state = ValidState();
        var oauthApps = Substitute.For<IPlatformOAuthAppResolver>();
        oauthApps.GetActiveCredentialForProviderAsync(
                "github",
                Arg.Any<CancellationToken>())
            .Returns(new ResolvedPlatformOAuthAppCredential(
                "github",
                "client-id",
                "client-secret",
                null,
                1));
        var github = Substitute.For<IGitHubOAuthClient>();
        github.ExchangeCodeAsync(
                Arg.Any<GitHubOAuthTokenRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new GitHubOAuthTokenResult(
                "plain-access",
                "plain-refresh",
                3600,
                "repo read:user",
                "bearer"));
        github.GetCurrentUserAsync("plain-access", Arg.Any<CancellationToken>())
            .Returns(new GitHubUserProfileResult("123", "octocat", "octocat@example.test"));
        var sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<UpsertOwnUserIntegrationConnectionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<UserIntegrationConnectionDto>.Success(
                DisconnectedDto() with
                {
                    Status = "connected",
                    ProviderUsername = "octocat"
                }));
        var handler = new CompleteGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(),
            ProtectorFor(state),
            oauthApps,
            github,
            sender);

        var result = await handler.Handle(
            new CompleteGitHubUserOAuthCommand(
                "code",
                "state",
                "https://tenant.test/callback"),
            default);

        Assert.True(result.IsSuccess, result.Error);
        await sender.Received(1).Send(
            Arg.Is<UpsertOwnUserIntegrationConnectionCommand>(command =>
                command.AccessToken == "plain-access" &&
                command.ProviderUserId == "123"),
            Arg.Any<CancellationToken>());
        await sender.DidNotReceive().Send(
            Arg.Any<UpsertTenantIntegrationCredentialCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upsert_encrypts_tokens_before_user_connection_is_saved()
    {
        var repository = new FakeUserConnectionRepository();
        var encryption = Substitute.For<IEncryptionService>();
        encryption.Encrypt("plain-access").Returns("encrypted-access");
        encryption.Encrypt("plain-refresh").Returns("encrypted-refresh");
        var handler = new UpsertOwnUserIntegrationConnectionCommandHandler(
            CurrentUser(),
            repository,
            encryption);

        var result = await handler.Handle(
            new UpsertOwnUserIntegrationConnectionCommand(
                "github",
                "123",
                "octocat",
                "octocat@example.test",
                "plain-access",
                "plain-refresh",
                DateTimeOffset.UtcNow.AddHours(1),
                ["repo"]),
            default);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(repository.Values);
        Assert.Equal("encrypted-access", stored.AccessTokenEncrypted);
        Assert.Equal("encrypted-refresh", stored.RefreshTokenEncrypted);
    }

    [Fact]
    public async Task Status_reads_only_current_users_connection()
    {
        var repository = new FakeUserConnectionRepository();
        repository.Values.Add(Connection(TenantId, UserId));
        repository.Values.Add(Connection(TenantId, Guid.NewGuid()));
        var handler = new GetGitHubUserIntegrationStatusQueryHandler(
            CurrentUser(),
            repository);

        var result = await handler.Handle(new GetGitHubUserIntegrationStatusQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserId, repository.LastUserId);
        Assert.Equal("octocat", result.Value!.ProviderUsername);
    }

    [Fact]
    public async Task Disconnect_updates_only_current_users_connection()
    {
        var otherUserId = Guid.NewGuid();
        var repository = new FakeUserConnectionRepository();
        var current = Connection(TenantId, UserId);
        var other = Connection(TenantId, otherUserId);
        repository.Values.Add(current);
        repository.Values.Add(other);
        var handler = new DisconnectOwnUserIntegrationCommandHandler(
            CurrentUser(),
            repository);

        var result = await handler.Handle(
            new DisconnectOwnUserIntegrationCommand("github"),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("disconnected", current.Status);
        Assert.NotNull(current.DisconnectedAt);
        Assert.Equal("connected", other.Status);
        Assert.Null(other.DisconnectedAt);
        Assert.Equal(UserId, repository.LastUserId);
    }

    [Fact]
    public async Task Tenant_enable_creates_tokenless_connected_approval()
    {
        var repository = Substitute.For<ITenantIntegrationCredentialRepository>();
        TenantIntegrationCredential? added = null;
        repository.AddAsync(
                Arg.Do<TenantIntegrationCredential>(value => added = value),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = new SetGitHubTenantApprovalCommandHandler(
            CurrentUser(),
            repository,
            Availability());

        var result = await handler.Handle(
            new SetGitHubTenantApprovalCommand(true),
            default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.IsEnabled);
        Assert.NotNull(added);
        Assert.Equal("connected", added.Status);
        Assert.Null(added.AccessTokenEncrypted);
        Assert.Null(added.RefreshTokenEncrypted);
    }

    [Fact]
    public async Task Tenant_disable_clears_legacy_tenant_token_state()
    {
        var approval = new TenantIntegrationCredential
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            IntegrationKey = "github",
            Status = "connected",
            AccessTokenEncrypted = "legacy-access",
            RefreshTokenEncrypted = "legacy-refresh",
            ScopesGranted = ["repo"],
            ConnectedAt = DateTimeOffset.UtcNow,
            ConnectedByUserId = UserId
        };
        var repository = Substitute.For<ITenantIntegrationCredentialRepository>();
        repository.GetByTenantAndIntegrationAsync(
                TenantId,
                "github",
                Arg.Any<CancellationToken>())
            .Returns(approval);
        var handler = new SetGitHubTenantApprovalCommandHandler(
            CurrentUser(),
            repository,
            Availability());

        var result = await handler.Handle(
            new SetGitHubTenantApprovalCommand(false),
            default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.IsEnabled);
        Assert.Equal("disconnected", approval.Status);
        Assert.Null(approval.AccessTokenEncrypted);
        Assert.Null(approval.RefreshTokenEncrypted);
    }

    [Fact]
    public async Task Refresh_uses_current_users_refresh_token_and_upserts_replacements()
    {
        var repository = new FakeUserConnectionRepository();
        var connection = Connection(TenantId, UserId);
        connection.RefreshTokenEncrypted = "encrypted-refresh";
        repository.Values.Add(connection);
        var oauthApps = Substitute.For<IPlatformOAuthAppResolver>();
        oauthApps.GetActiveCredentialForProviderAsync(
                "github",
                Arg.Any<CancellationToken>())
            .Returns(new ResolvedPlatformOAuthAppCredential(
                "github",
                "client-id",
                "client-secret",
                null,
                1));
        var github = Substitute.For<IGitHubOAuthClient>();
        github.RefreshTokenAsync(
                Arg.Any<GitHubOAuthRefreshRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new GitHubOAuthTokenResult(
                "replacement-access",
                "replacement-refresh",
                3600,
                "repo",
                "bearer"));
        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt("encrypted-refresh").Returns("plain-refresh");
        var sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<UpsertOwnUserIntegrationConnectionCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<UserIntegrationConnectionDto>.Success(
                DisconnectedDto() with { Status = "connected" }));
        var handler = new RefreshOwnGitHubConnectionCommandHandler(
            CurrentUser(),
            repository,
            Availability(),
            oauthApps,
            github,
            encryption,
            sender);

        var result = await handler.Handle(
            new RefreshOwnGitHubConnectionCommand(),
            default);

        Assert.True(result.IsSuccess, result.Error);
        await github.Received(1).RefreshTokenAsync(
            Arg.Is<GitHubOAuthRefreshRequest>(request =>
                request.RefreshToken == "plain-refresh"),
            Arg.Any<CancellationToken>());
        await sender.Received(1).Send(
            Arg.Is<UpsertOwnUserIntegrationConnectionCommand>(command =>
                command.AccessToken == "replacement-access" &&
                command.RefreshToken == "replacement-refresh"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("https://evil.test")]
    [InlineData("//evil.test")]
    [InlineData("/safe\\evil")]
    public void Return_url_rule_rejects_non_local_values(string value)
    {
        Assert.Null(GitHubUserOAuthRules.ValidateReturnUrl(value));
    }

    private static CompleteGitHubUserOAuthCommandHandler CompleteHandler(
        IOAuthStateProtector protector)
    {
        return new CompleteGitHubUserOAuthCommandHandler(
            CurrentUser(),
            Availability(),
            protector,
            Substitute.For<IPlatformOAuthAppResolver>(),
            Substitute.For<IGitHubOAuthClient>(),
            Substitute.For<ISender>());
    }

    private static ICurrentUser CurrentUser()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.TenantId.Returns(TenantId);
        currentUser.UserId.Returns(UserId);
        currentUser.SessionBinding.Returns("session-hash");
        return currentUser;
    }

    private static GitHubUserIntegrationAvailability Availability(
        bool isActive = true,
        IReadOnlyList<string>? activeModules = null,
        string connectionScope = "both",
        string? tenantApprovalStatus = "connected")
    {
        var catalog = Substitute.For<IIntegrationCatalogRepository>();
        catalog.GetByKeyAsync("github", Arg.Any<CancellationToken>()).Returns(
            new IntegrationCatalogEntry
            {
                IntegrationKey = "github",
                IsActive = isActive,
                ConnectionScope = connectionScope,
                OnevoAppProvider = "github"
            });
        catalog.GetLinkedModuleKeysAsync("github", Arg.Any<CancellationToken>())
            .Returns(new[] { "integrations" });
        var apps = Substitute.For<IPlatformOAuthAppResolver>();
        apps.GetActiveAppForProviderAsync("github", Arg.Any<CancellationToken>()).Returns(
            new ResolvedPlatformOAuthApp(
                "github",
                "client-id",
                "https://github.com/login/oauth/authorize",
                "https://github.com/login/oauth/access_token",
                new[] { "repo" }));
        var entitlements = Substitute.For<IModuleEntitlementService>();
        entitlements.GetActiveModuleKeysForTenantAsync(
                TenantId,
                Arg.Any<CancellationToken>())
            .Returns(activeModules ?? new[] { "integrations" });

        var tenantIntegrations = Substitute.For<ITenantIntegrationCredentialRepository>();
        var approval = tenantApprovalStatus is null
            ? null
            : new TenantIntegrationCredential
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                IntegrationKey = "github",
                Status = tenantApprovalStatus,
                ScopesGranted = [],
                ConnectedAt = DateTimeOffset.UtcNow,
                ConnectedByUserId = UserId
            };
        tenantIntegrations.GetByTenantAndIntegrationAsync(
                TenantId,
                "github",
                Arg.Any<CancellationToken>())
            .Returns(approval);

        return new GitHubUserIntegrationAvailability(
            catalog,
            apps,
            entitlements,
            tenantIntegrations);
    }

    private static GitHubOAuthState ValidState()
    {
        var now = DateTimeOffset.UtcNow;
        return new GitHubOAuthState(
            "nonce",
            TenantId,
            UserId,
            "github",
            "github",
            "/settings",
            now.AddMinutes(-1),
            now.AddMinutes(5),
            "session-hash");
    }

    private static IOAuthStateProtector ProtectorFor(GitHubOAuthState state)
    {
        return new FixedStateProtector(state);
    }

    private static UserIntegrationConnection Connection(Guid tenantId, Guid userId)
    {
        return new UserIntegrationConnection
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            IntegrationKey = "github",
            ProviderUserId = "123",
            ProviderUsername = "octocat",
            Status = "connected",
            ConnectedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ScopesGranted = ["repo"]
        };
    }

    private static UserIntegrationConnectionDto DisconnectedDto()
    {
        return new UserIntegrationConnectionDto(
            "github",
            "disconnected",
            null,
            null,
            null,
            null,
            [],
            null,
            null,
            null,
            null);
    }

    private sealed class FakeUserConnectionRepository : IUserIntegrationConnectionRepository
    {
        public List<UserIntegrationConnection> Values { get; } = [];
        public Guid? LastTenantId { get; private set; }
        public Guid? LastUserId { get; private set; }

        public Task<UserIntegrationConnection?> GetActiveAsync(
            Guid tenantId,
            Guid userId,
            string integrationKey,
            CancellationToken ct)
        {
            LastTenantId = tenantId;
            LastUserId = userId;
            var value = Values.FirstOrDefault(connection =>
                connection.TenantId == tenantId &&
                connection.UserId == userId &&
                connection.IntegrationKey == integrationKey &&
                connection.DisconnectedAt == null);
            return Task.FromResult(value);
        }

        public Task AddAsync(UserIntegrationConnection connection, CancellationToken ct)
        {
            Values.Add(connection);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedStateProtector : IOAuthStateProtector
    {
        private readonly GitHubOAuthState _state;

        public FixedStateProtector(GitHubOAuthState state)
        {
            _state = state;
        }

        public string Protect(GitHubOAuthState state)
        {
            return "state";
        }

        public bool TryUnprotect(string protectedState, out GitHubOAuthState? state)
        {
            state = _state;
            return protectedState == "state";
        }
    }
}
