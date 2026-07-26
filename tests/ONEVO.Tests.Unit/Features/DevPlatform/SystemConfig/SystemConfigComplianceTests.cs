using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.PaymentGateway.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public class SystemConfigComplianceTests
{
    [Fact]
    public void Schema_PaymentGatewayEntities_HaveOnlyInventoryColumns()
    {
        // Assert PaymentGatewayConfig has exact expected properties
        var configProps = typeof(PaymentGatewayConfig).GetProperties().Select(p => p.Name).ToList();
        var expectedConfigProps = new[] { "Id", "GatewayKey", "Provider", "Environment", "DisplayName", "LogoUrl", "PublicKey", "MerchantId", "WebhookUrl", "IsActive", "CreatedById", "CreatedAt", "UpdatedAt", "Credentials", "CountryRoutes" };
        foreach (var prop in configProps)
            Assert.Contains(prop, expectedConfigProps);

        // Assert PaymentGatewayCredential has exact expected properties
        var credProps = typeof(PaymentGatewayCredential).GetProperties().Select(p => p.Name).ToList();
        var expectedCredProps = new[] { "Id", "PaymentGatewayConfigId", "SecretEncrypted", "WebhookSecretEncrypted", "EncryptionKeyVersion", "CredentialVersion", "IsActive", "RotatedById", "RotatedAt", "DeactivatedById", "DeactivatedAt", "GatewayConfig" };
        foreach (var prop in credProps)
            Assert.Contains(prop, expectedCredProps);

        // Assert PaymentGatewayCountryRoute has exact expected properties
        var routeProps = typeof(PaymentGatewayCountryRoute).GetProperties().Select(p => p.Name).ToList();
        var expectedRouteProps = new[] { "Id", "CountryCode", "CountryNameSnapshot", "GatewayConfigId", "Environment", "IsActive", "CreatedById", "CreatedAt", "UpdatedAt", "GatewayConfig" };
        foreach (var prop in routeProps)
            Assert.Contains(prop, expectedRouteProps);
    }

    [Fact]
    public void Schema_IllegalTables_AreNotPresentInDomain()
    {
        var domainTypes = typeof(PaymentGatewayConfig).Assembly.GetTypes();
        // PlatformServiceKey is now a Phase 1 canonical table (platform_service_keys)
        // per phase1-table-inventory.md - no longer illegal.
        var illegalNames = new[] { "AiProviderConfig", "TenantAiProviderOverride", "EmailProviderConfig", "SendgridConfig", "ResendConfig" };

        foreach (var type in domainTypes)
        {
            foreach (var illegal in illegalNames)
            {
                Assert.False(type.Name.Contains(illegal, StringComparison.OrdinalIgnoreCase), $"Found illegal entity: {type.Name}");
            }
        }
    }

    [Fact]
    public async Task Security_CreateCommand_EncryptsCredentialsBeforeSave()
    {
        var encryptionMock = new Mock<IEncryptionService>();
        encryptionMock.Setup(e => e.EncryptBytes(It.IsAny<string>()))
            .Returns(new byte[] { 1, 2, 3 });

        var repoMock = new Mock<IPaymentGatewayRepository>();
        repoMock.Setup(r => r.GetByGatewayKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentGatewayConfig?)null);
        
        var handler = new CreatePaymentGatewayCommandHandler(repoMock.Object, encryptionMock.Object);

        var request = new CreatePaymentGatewayCommand(
            "test-key", "stripe", "sandbox", "Test", null, null, null, null, true,
            "plain-secret", "webhook-secret", new List<string>(), new List<string?>(), Guid.NewGuid());

        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        encryptionMock.Verify(e => e.EncryptBytes(It.Is<string>(b => b.Length > 0)), Times.Exactly(2));
    }

    [Fact]
    public async Task Security_RotateCommand_EncryptsCredentialsBeforeSave()
    {
        var encryptionMock = new Mock<IEncryptionService>();
        encryptionMock.Setup(e => e.EncryptBytes(It.IsAny<string>()))
            .Returns(new byte[] { 1, 2, 3 });

        var repoMock = new Mock<IPaymentGatewayRepository>();
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentGatewayConfig());
        repoMock.Setup(r => r.GetActiveCredentialsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PaymentGatewayCredential { IsActive = true }
            ]);

        var handler = new RotatePaymentGatewayCredentialCommandHandler(repoMock.Object, encryptionMock.Object);

        var request = new RotatePaymentGatewayCredentialCommand(Guid.NewGuid(), "new-secret", "new-webhook", Guid.NewGuid());
        var result = await handler.Handle(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        encryptionMock.Verify(e => e.EncryptBytes(It.Is<string>(b => b.Length > 0)), Times.Exactly(2));
    }

    [Fact]
    public void Security_DtoResponses_DoNotExposeCredentials()
    {
        var props = typeof(PaymentGatewayConfigDto).GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
        
        Assert.DoesNotContain(props, p => p.Contains("secret"));
        Assert.DoesNotContain(props, p => p.Contains("password"));
        Assert.DoesNotContain(props, p => p.Contains("credential") && !p.Contains("activecredential")); // allows HasActiveCredential, ActiveCredentialVersion
    }

    [Fact]
    public void Authorization_SystemConfigEndpoints_RequireAdminPolicyAndCorrectPermission()
    {
        var controllerType = typeof(SystemConfigPaymentGatewayController);
        
        // Controller must have AdminPolicy
        var authorizeAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("AdminPolicy", authorizeAttr.Policy);

        // Methods must have specific permissions
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
            Assert.NotNull(permissionAttr);

            if (method.Name == "ListGateways")
                Assert.Equal(PlatformPermissionCatalog.SystemConfigRead, permissionAttr.Permission);
            else if (method.Name == "ResolveForCountry")
                Assert.Equal(PlatformPermissionCatalog.TenantsManage, permissionAttr.Permission);
            else // CreateGateway, RotateCredential, VerifyCredentials
                Assert.Equal(PlatformPermissionCatalog.SystemConfigManage, permissionAttr.Permission);
        }
    }
}
