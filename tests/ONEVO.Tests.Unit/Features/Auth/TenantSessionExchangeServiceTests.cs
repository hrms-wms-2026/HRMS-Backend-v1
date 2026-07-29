using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Services;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class TenantSessionExchangeServiceTests
{
    private sealed class Fixture
    {
        public Mock<ITenantSessionExchangeChallengeRepository> Challenges { get; } = new();
        public Mock<ISecureTokenGenerator> Tokens { get; } = new();
        public Mock<ILoginSessionMaterialFactory> SessionMaterialFactory { get; } = new();
        public Mock<ITenantRepository> Tenants { get; } = new();
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public IConfiguration Configuration { get; private set; } = MakeConfiguration();

        public Fixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-29T12:00:00Z"));
            Tokens.Setup(t => t.HashToken(It.IsAny<string>())).Returns((string raw) => $"hash({raw})");
        }

        public Fixture WithConfiguration(string? rootDomain, string? appBaseUrl)
        {
            Configuration = MakeConfiguration(rootDomain, appBaseUrl);
            return this;
        }

        private static IConfiguration MakeConfiguration(string? rootDomain = "localhost", string? appBaseUrl = "http://localhost:4200")
        {
            var data = new Dictionary<string, string?>();
            if (rootDomain is not null) data["Tenancy:RootDomain"] = rootDomain;
            if (appBaseUrl is not null) data["Urls:AppBaseUrl"] = appBaseUrl;
            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        public TenantSessionExchangeService Build() => new(
            Challenges.Object, Tokens.Object, SessionMaterialFactory.Object,
            Tenants.Object, Users.Object, UnitOfWork.Object, Clock.Object, Configuration);
    }

    private static Tenant ActiveTenant(Guid id, string slug = "acme") => new()
    {
        Id = id, Name = "Acme Inc", Slug = slug, CompanySizeRange = "51-200", Status = TenantStatus.Active
    };

    private static User ActiveUser(Guid tenantId, Guid userId) => new()
    {
        Id = userId, TenantId = tenantId, Email = "owner@acme.test", PasswordHash = "irrelevant",
        FirstName = "Jane", LastName = "Doe", IsActive = true
    };

    // ---- CreateAsync ----

    [Fact]
    public async Task CreateAsync_PersistsOnlyHashedCode_NeverRawCode()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-exchange-code");
        TenantSessionExchangeChallenge? captured = null;
        fixture.Challenges
            .Setup(c => c.AddAsync(It.IsAny<TenantSessionExchangeChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<TenantSessionExchangeChallenge, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        await fixture.Build().CreateAsync(user, tenant, "password", "1.2.3.4", "ua", default);

        captured.Should().NotBeNull();
        captured!.CodeHash.Should().Be("hash(raw-exchange-code)");
        captured.CodeHash.Should().NotBe("raw-exchange-code");
        captured.TenantId.Should().Be(tenant.Id);
        captured.UserId.Should().Be(user.Id);
        captured.AuthOrigin.Should().Be("password");
        captured.IpAddress.Should().Be("1.2.3.4");
        captured.UserAgent.Should().Be("ua");
        captured.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ReturnsExchangeResponse_WithContinueUrlAndWorkspace_NoSessionMaterial()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid(), "acme");
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-exchange-code");

        var result = await fixture.Build().CreateAsync(user, tenant, "password", null, null, default);

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.RequiresTenantSessionExchange.Should().BeTrue();
        dto.Workspace.Should().NotBeNull();
        dto.Workspace!.Slug.Should().Be("acme");
        dto.Workspace.DisplayName.Should().Be("Acme Inc");
        dto.TenantSessionExchange!.ContinueUrl.Should().Be("http://acme.localhost:4200/auth/continue?code=raw-exchange-code");
        dto.TenantSessionExchange.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-07-29T12:02:00Z"));
        // Never signed in on the base host: no CSRF/session material at all.
        dto.CsrfToken.Should().BeEmpty();
        dto.CsrfTokenHash.Should().BeEmpty();
        dto.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ExchangeResponse_SerializesNoTenantIdOrUserIdOrSessionMaterial()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-exchange-code");

        var result = await fixture.Build().CreateAsync(user, tenant, "password", null, null, default);

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value!.ToTenantSessionExchangeResponse());

        json.Should().NotContain(tenant.Id.ToString());
        json.Should().NotContain(user.Id.ToString());
        json.Should().NotContain("tenant_id");
        json.Should().NotContain("user_id");
        json.Should().NotContain("access_token");
        json.Should().NotContain("refresh_token");
        json.Should().NotContain("jwt");
        json.Should().Contain("\"redirect_required\":true");
        json.Should().Contain("\"authenticated\":false");
        json.Should().Contain(user.Email);
    }

    [Fact]
    public async Task CreateAsync_MissingRootDomain_Throws()
    {
        var fixture = new Fixture().WithConfiguration(rootDomain: null, appBaseUrl: "http://localhost:4200");
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Tokens.Setup(t => t.GenerateOpaqueToken()).Returns("raw-exchange-code");

        var action = () => fixture.Build().CreateAsync(user, tenant, "password", null, null, default);

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    // ---- ConsumeAsync ----

    [Fact]
    public async Task ConsumeAsync_EmptyCode_ReturnsGenericFailure_WithoutHashingOrQuerying()
    {
        var fixture = new Fixture();

        var result = await fixture.Build().ConsumeAsync(string.Empty, Guid.NewGuid(), null, null, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.Challenges.Verify(
            c => c.TryConsumeAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_UnknownOrExpiredOrWrongTenantCode_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        var tenantId = Guid.NewGuid();
        fixture.Challenges
            .Setup(c => c.TryConsumeAsync("hash(bad-code)", tenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSessionExchangeChallengeState?)null);

        var result = await fixture.Build().ConsumeAsync("bad-code", tenantId, null, null, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ConsumeAsync_TenantInactive_ReturnsGenericFailure_WithoutCreatingSession()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        tenant.Status = TenantStatus.Cancelled;
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        fixture.Challenges
            .Setup(c => c.TryConsumeAsync("hash(code)", tenant.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSessionExchangeChallengeState(tenant.Id, user.Id, "password"));
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var result = await fixture.Build().ConsumeAsync("code", tenant.Id, null, null, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        fixture.SessionMaterialFactory.Verify(
            f => f.PrepareAsync(It.IsAny<User>(), It.IsAny<Tenant>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConsumeAsync_UserInactive_ReturnsGenericFailure()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        user.IsActive = false;
        fixture.Challenges
            .Setup(c => c.TryConsumeAsync("hash(code)", tenant.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSessionExchangeChallengeState(tenant.Id, user.Id, "password"));
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var result = await fixture.Build().ConsumeAsync("code", tenant.Id, null, null, default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task ConsumeAsync_Success_ReturnsSignedInSession_AndUpdatesLastLoginAt()
    {
        var fixture = new Fixture();
        var tenant = ActiveTenant(Guid.NewGuid());
        var user = ActiveUser(tenant.Id, Guid.NewGuid());
        var sessionDto = new LoginResponseDto(
            "csrf-raw", "csrf-hash", DateTimeOffset.UtcNow.AddHours(8),
            new CurrentUserDto(user.Id, user.TenantId, user.Email));
        fixture.Challenges
            .Setup(c => c.TryConsumeAsync("hash(code)", tenant.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSessionExchangeChallengeState(tenant.Id, user.Id, "password"));
        fixture.Tenants.Setup(t => t.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        fixture.Users.Setup(u => u.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        fixture.SessionMaterialFactory
            .Setup(f => f.PrepareAsync(user, tenant, "1.2.3.4", "ua", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LoginResponseDto>.Success(sessionDto));

        var result = await fixture.Build().ConsumeAsync("code", tenant.Id, "1.2.3.4", "ua", default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(sessionDto);
        result.Value!.RequiresTenantSessionExchange.Should().BeFalse();
        user.LastLoginAt.Should().Be(fixture.Clock.Object.UtcNow);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
