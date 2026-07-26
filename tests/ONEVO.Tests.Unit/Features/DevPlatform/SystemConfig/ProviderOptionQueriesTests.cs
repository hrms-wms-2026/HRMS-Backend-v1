using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListOAuthProviderOptions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListPaymentGatewayProviderOptions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.Queries.ListServiceKeyProviderOptions;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.SystemConfig;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public sealed class ProviderOptionQueriesTests
{
    [Fact]
    public async Task ServiceKeyProviders_ReturnsSeededServiceKeyProviderOptions()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail),
            Provider("resend", "Resend", PlatformProviderFamilies.TransactionalEmail),
            Provider("cloudflare", "Cloudflare", PlatformProviderFamilies.Infrastructure),
            Provider("cloudflare_r2", "Cloudflare R2", PlatformProviderFamilies.ObjectStorage),
            Provider("aws_rekognition", "AWS Rekognition", PlatformProviderFamilies.AiVerification),
            Provider("google", "Google", PlatformProviderFamilies.OAuthApp),
            Provider("stripe", "Stripe", PlatformProviderFamilies.PaymentGateway));
        await db.SaveChangesAsync();

        var handler = new ListServiceKeyProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListServiceKeyProviderOptionsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var providerKeys = result.Value!.Select(option => option.ProviderKey).Order().ToArray();
        Assert.Equal(
            new[] { "aws_rekognition", "cloudflare", "cloudflare_r2", "resend", "sendgrid" },
            providerKeys);
    }

    [Fact]
    public async Task ServiceKeyProviders_MarksSendgridConfiguredAndActive_WhenActiveServiceKeyExists()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.Add(Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail));
        db.PlatformServiceKeys.Add(ServiceKey("sendgrid", isActive: true));
        await db.SaveChangesAsync();

        var handler = new ListServiceKeyProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListServiceKeyProviderOptionsQuery(), CancellationToken.None);

        var sendgrid = Assert.Single(result.Value!, option => option.ProviderKey == "sendgrid");
        Assert.True(sendgrid.Configured);
        Assert.True(sendgrid.IsActive);
    }

    [Fact]
    public async Task ServiceKeyProviders_MarksResendConfiguredFalseAndActiveFalse_WhenNoRowExists()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.Add(Provider("resend", "Resend", PlatformProviderFamilies.TransactionalEmail));
        await db.SaveChangesAsync();

        var handler = new ListServiceKeyProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListServiceKeyProviderOptionsQuery(), CancellationToken.None);

        var resend = Assert.Single(result.Value!, option => option.ProviderKey == "resend");
        Assert.False(resend.Configured);
        Assert.False(resend.IsActive);
    }

    [Fact]
    public async Task OAuthProviders_ReturnsGoogleGithubMicrosoftZoomOnly()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("google", "Google", PlatformProviderFamilies.OAuthApp),
            Provider("github", "GitHub", PlatformProviderFamilies.OAuthApp),
            Provider("microsoft", "Microsoft", PlatformProviderFamilies.OAuthApp),
            Provider("zoom", "Zoom", PlatformProviderFamilies.OAuthApp),
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail),
            Provider("stripe", "Stripe", PlatformProviderFamilies.PaymentGateway));
        await db.SaveChangesAsync();

        var handler = new ListOAuthProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListOAuthProviderOptionsQuery(), CancellationToken.None);

        var providerKeys = result.Value!.Select(option => option.ProviderKey).Order().ToArray();
        Assert.Equal(new[] { "github", "google", "microsoft", "zoom" }, providerKeys);
    }

    [Fact]
    public async Task OAuthProviders_MarksGoogleConfiguredAndActive_WhenActiveOAuthAppAndCredentialExist()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.Add(Provider("google", "Google", PlatformProviderFamilies.OAuthApp));
        var app = OAuthApp("google", isActive: true);
        db.PlatformOAuthApps.Add(app);
        db.PlatformOAuthAppCredentials.Add(OAuthCredential(app.Id, isActive: true));
        await db.SaveChangesAsync();

        var handler = new ListOAuthProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListOAuthProviderOptionsQuery(), CancellationToken.None);

        var google = Assert.Single(result.Value!, option => option.ProviderKey == "google");
        Assert.True(google.Configured);
        Assert.True(google.IsActive);
    }

    [Fact]
    public async Task PaymentGatewayProviders_ReturnsStripePayherePaddleOnly()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("stripe", "Stripe", PlatformProviderFamilies.PaymentGateway),
            Provider("payhere", "PayHere", PlatformProviderFamilies.PaymentGateway),
            Provider("paddle", "Paddle", PlatformProviderFamilies.PaymentGateway),
            Provider("google", "Google", PlatformProviderFamilies.OAuthApp),
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail));
        await db.SaveChangesAsync();

        var handler = new ListPaymentGatewayProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListPaymentGatewayProviderOptionsQuery(), CancellationToken.None);

        var providerKeys = result.Value!.Select(option => option.ProviderKey).Order().ToArray();
        Assert.Equal(new[] { "paddle", "payhere", "stripe" }, providerKeys);
    }

    [Fact]
    public async Task PaymentGatewayProviders_MarksStripeConfiguredAndActive_WhenActiveConfigAndCredentialExist()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.Add(Provider("stripe", "Stripe", PlatformProviderFamilies.PaymentGateway));
        var config = PaymentGatewayConfig("stripe", isActive: true);
        db.PaymentGatewayConfigs.Add(config);
        db.PaymentGatewayCredentials.Add(PaymentGatewayCredential(config.Id, isActive: true));
        await db.SaveChangesAsync();

        var handler = new ListPaymentGatewayProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListPaymentGatewayProviderOptionsQuery(), CancellationToken.None);

        var stripe = Assert.Single(result.Value!, option => option.ProviderKey == "stripe");
        Assert.True(stripe.Configured);
        Assert.True(stripe.IsActive);
    }

    [Fact]
    public async Task InactiveProviderMetadataRows_AreNotReturned()
    {
        await using var db = BuildInMemoryDb();
        db.PlatformProviders.AddRange(
            Provider("sendgrid", "SendGrid", PlatformProviderFamilies.TransactionalEmail, isActive: false),
            Provider("resend", "Resend", PlatformProviderFamilies.TransactionalEmail, isActive: true));
        await db.SaveChangesAsync();

        var handler = new ListServiceKeyProviderOptionsQueryHandler(new EfPlatformProviderRepository(db));

        var result = await handler.Handle(new ListServiceKeyProviderOptionsQuery(), CancellationToken.None);

        var providerKeys = result.Value!.Select(option => option.ProviderKey).ToArray();
        Assert.DoesNotContain("sendgrid", providerKeys);
        Assert.Contains("resend", providerKeys);
    }

    [Fact]
    public void ProviderOptionDto_ContainsNoSecretShapedField()
    {
        var forbiddenFragments = new[]
        {
            "secret", "credential", "token", "password", "apikey", "api_key",
            "privatekey", "private_key", "encrypted", "webhooksecret", "clientsecret",
            "providerfamily"
        };

        var propertyNames = typeof(ProviderOptionDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name.ToLowerInvariant())
            .ToArray();

        Assert.Equal(
            new[] { "configured", "displayname", "isactive", "providerkey" },
            propertyNames.Order().ToArray());

        Assert.DoesNotContain(
            propertyNames,
            name => forbiddenFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)));

        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "id", StringComparison.Ordinal));
    }

    private static PlatformProvider Provider(
        string providerKey,
        string displayName,
        string family,
        bool isActive = true) =>
        new()
        {
            Id = Guid.NewGuid(),
            ProviderKey = providerKey,
            DisplayName = displayName,
            ProviderFamily = family,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PlatformServiceKey ServiceKey(string serviceKey, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            ServiceKey = serviceKey,
            DisplayName = serviceKey,
            ApiKeyEncrypted = $"encrypted-{serviceKey}",
            IsActive = isActive,
            UpdatedById = Guid.NewGuid(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PlatformOAuthApp OAuthApp(string provider, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            AppName = provider,
            ClientId = $"client-{provider}",
            AuthorizationUrl = "https://example.com/authorize",
            TokenUrl = "https://example.com/token",
            IsActive = isActive,
            UpdatedById = Guid.NewGuid(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PlatformOAuthAppCredential OAuthCredential(Guid platformOAuthAppId, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            PlatformOAuthAppId = platformOAuthAppId,
            ClientSecretEncrypted = "encrypted-secret",
            EncryptionKeyVersion = "v1",
            CredentialVersion = 1,
            IsActive = isActive,
            RotatedById = Guid.NewGuid(),
            RotatedAt = DateTimeOffset.UtcNow
        };

    private static PaymentGatewayConfig PaymentGatewayConfig(string provider, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            GatewayKey = $"{provider}-production",
            Provider = provider,
            Environment = "production",
            DisplayName = provider,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PaymentGatewayCredential PaymentGatewayCredential(Guid gatewayConfigId, bool isActive) =>
        new()
        {
            Id = Guid.NewGuid(),
            PaymentGatewayConfigId = gatewayConfigId,
            SecretEncrypted = System.Text.Encoding.UTF8.GetBytes("encrypted-secret"),
            EncryptionKeyVersion = "v1",
            CredentialVersion = 1,
            IsActive = isActive,
            RotatedById = Guid.NewGuid(),
            RotatedAt = DateTimeOffset.UtcNow
        };

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var clock = Mock.Of<IDateTimeProvider>();

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(Mock.Of<ICurrentUser>(), clock),
            new SoftDeleteInterceptor(clock),
            new DomainEventDispatchInterceptor(Mock.Of<MediatR.IPublisher>()),
            Mock.Of<ITenantContext>());
    }
}
