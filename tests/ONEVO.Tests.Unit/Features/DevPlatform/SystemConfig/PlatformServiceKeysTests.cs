using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Api.Controllers.Admin.DevPlatform.SystemConfig;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformProviders.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.SetPlatformServiceKeyActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.UpdatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.VerifyPlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Helpers;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.GetPlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Queries.ListPlatformServiceKeys;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Entities;
using ONEVO.Domain.Features.DevPlatform.SystemConfig.PlatformProviders.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SystemConfig;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.SystemConfig;

public class PlatformServiceKeysTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    private static PlatformServiceKey ExistingKey(string serviceKey = "resend", bool isActive = true) => new()
    {
        Id = Guid.NewGuid(),
        ServiceKey = serviceKey,
        DisplayName = "Resend",
        ApiKeyEncrypted = "OLD-ENCRYPTED",
        IsActive = isActive,
        LastVerifiedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedById = Guid.NewGuid(),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
    };

    private static IPlatformProviderRepository ProviderRepository()
    {
        var repository = new Mock<IPlatformProviderRepository>();
        repository
            .Setup(repo => repo.GetActiveFamilyAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string providerKey, CancellationToken _) => providerKey switch
            {
                "resend" or "sendgrid" => PlatformProviderFamilies.TransactionalEmail,
                "cloudflare" => PlatformProviderFamilies.Infrastructure,
                "cloudflare_r2" => PlatformProviderFamilies.ObjectStorage,
                "aws_rekognition" => PlatformProviderFamilies.AiVerification,
                _ => null
            });
        return repository.Object;
    }

    // 1. Entity / EF mapping

    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    [Fact]
    public void EfModel_PlatformServiceKeys_TableMappedWithUniqueServiceKeyAndRequiredEncryptedColumn()
    {
        using var db = BuildInMemoryDb();

        var entityType = db.Model.FindEntityType(typeof(PlatformServiceKey));
        Assert.NotNull(entityType);
        Assert.Equal("platform_service_keys", entityType!.GetTableName());

        // UNIQUE(service_key)
        var serviceKeyProp = entityType.FindProperty(nameof(PlatformServiceKey.ServiceKey));
        Assert.NotNull(serviceKeyProp);
        Assert.False(serviceKeyProp!.IsNullable);
        Assert.Equal(50, serviceKeyProp.GetMaxLength());
        var uniqueIndex = entityType.GetIndexes()
            .FirstOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(PlatformServiceKey.ServiceKey));
        Assert.NotNull(uniqueIndex);
        Assert.True(uniqueIndex!.IsUnique);

        // api_key_encrypted mapped and NOT NULL
        var encryptedProp = entityType.FindProperty(nameof(PlatformServiceKey.ApiKeyEncrypted));
        Assert.NotNull(encryptedProp);
        Assert.False(encryptedProp!.IsNullable);

        // Remaining inventory columns present
        Assert.NotNull(entityType.FindProperty(nameof(PlatformServiceKey.DisplayName)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformServiceKey.IsActive)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformServiceKey.LastVerifiedAt)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformServiceKey.UpdatedById)));
        Assert.NotNull(entityType.FindProperty(nameof(PlatformServiceKey.UpdatedAt)));
    }

    [Fact]
    public void Schema_PlatformServiceKeyEntity_HasOnlyInventoryColumns()
    {
        var props = typeof(PlatformServiceKey).GetProperties().Select(p => p.Name).ToList();
        var expected = new[] { "Id", "ServiceKey", "DisplayName", "ApiKeyEncrypted", "IsActive", "LastVerifiedAt", "UpdatedById", "UpdatedAt" };
        foreach (var prop in props)
            Assert.Contains(prop, expected);
        foreach (var exp in expected)
            Assert.Contains(exp, props);
    }

    // 2. Command tests

    [Fact]
    public async Task Create_EncryptsApiKeyBeforeRepositorySave()
    {
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("plain-api-key")).Returns("ENCRYPTED-VALUE");

        PlatformServiceKey? saved = null;
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey>());
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformServiceKey>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformServiceKey, CancellationToken>((k, _) => saved = k)
            .Returns(Task.CompletedTask);

        var handler = new CreatePlatformServiceKeyCommandHandler(
            repo.Object, ProviderRepository(), encryption.Object);
        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand("resend", "Resend", "plain-api-key", Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal("ENCRYPTED-VALUE", saved!.ApiKeyEncrypted);
        Assert.True(saved.IsActive);
        encryption.Verify(e => e.Encrypt("plain-api-key"), Times.Once);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateServiceKey_ReturnsConflict()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingKey());

        var handler = new CreatePlatformServiceKeyCommandHandler(
            repo.Object, ProviderRepository(), Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand("resend", "Resend", "some-key", Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Theory]
    [InlineData("mailchimp")]     // not in allowlist
    [InlineData("Resend")]        // not a lowercase slug
    [InlineData("")]              // empty
    public async Task Create_UnsupportedOrInvalidServiceKey_ReturnsValidationError(string serviceKey)
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        var handler = new CreatePlatformServiceKeyCommandHandler(
            repo.Object, ProviderRepository(), Mock.Of<IEncryptionService>());

        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand(serviceKey, "Name", "some-key", Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.AddAsync(It.IsAny<PlatformServiceKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_MissingApiKey_ReturnsValidationError()
    {
        var handler = new CreatePlatformServiceKeyCommandHandler(
            Mock.Of<IPlatformServiceKeyRepository>(),
            ProviderRepository(),
            Mock.Of<IEncryptionService>());

        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand("resend", "Resend", "", Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task Create_ActiveSendgridWhileActiveResendExists_ReturnsConflict()
    {
        var activeResend = ExistingKey("resend", isActive: true);
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("sendgrid", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey> { activeResend });

        var handler = new CreatePlatformServiceKeyCommandHandler(
            repo.Object, ProviderRepository(), Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand("sendgrid", "SendGrid", "some-key", Actor),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        repo.Verify(r => r.AddAsync(It.IsAny<PlatformServiceKey>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_NonEmailServiceKey_IgnoresActiveTransactionalEmailProviders()
    {
        var activeResend = ExistingKey("resend", isActive: true);
        PlatformServiceKey? saved = null;
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("cloudflare", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<PlatformServiceKey>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformServiceKey, CancellationToken>((k, _) => saved = k)
            .Returns(Task.CompletedTask);

        var handler = new CreatePlatformServiceKeyCommandHandler(
            repo.Object, ProviderRepository(), Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new CreatePlatformServiceKeyCommand("cloudflare", "Cloudflare", "some-key", Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Rotate_ReplacesEncryptedValue_AndNeverExposesPlaintext()
    {
        var entity = ExistingKey();
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Encrypt("new-plain-key")).Returns("NEW-ENCRYPTED");

        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var handler = new RotatePlatformServiceKeyCommandHandler(repo.Object, encryption.Object);
        var result = await handler.Handle(
            new RotatePlatformServiceKeyCommand("resend", "new-plain-key", Actor),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-ENCRYPTED", entity.ApiKeyEncrypted);
        Assert.Null(entity.LastVerifiedAt); // new key not yet verified
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Response DTO carries no key material at all
        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("new-plain-key", serialized);
        Assert.DoesNotContain("NEW-ENCRYPTED", serialized);
    }

    [Fact]
    public async Task Rotate_UnknownServiceKey_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);

        var handler = new RotatePlatformServiceKeyCommandHandler(repo.Object, Mock.Of<IEncryptionService>());
        var result = await handler.Handle(
            new RotatePlatformServiceKeyCommand("sendgrid", "some-key", Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetActivation_ChangesIsActive(bool target)
    {
        var entity = ExistingKey(isActive: !target);
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey> { entity });

        var handler = new SetPlatformServiceKeyActivationCommandHandler(
            repo.Object, ProviderRepository());
        var result = await handler.Handle(
            new SetPlatformServiceKeyActivationCommand("resend", target, Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(target, entity.IsActive);
        Assert.Equal(Actor, entity.UpdatedById);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetActivation_UnknownServiceKey_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);

        var handler = new SetPlatformServiceKeyActivationCommandHandler(
            repo.Object, ProviderRepository());
        var result = await handler.Handle(
            new SetPlatformServiceKeyActivationCommand("cloudflare", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task SetActivation_ActivatingResendWhileActiveSendgridExists_ReturnsConflict()
    {
        var inactiveResend = ExistingKey("resend", isActive: false);
        var activeSendgrid = ExistingKey("sendgrid", isActive: true);
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveResend);
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey> { inactiveResend, activeSendgrid });

        var handler = new SetPlatformServiceKeyActivationCommandHandler(
            repo.Object, ProviderRepository());
        var result = await handler.Handle(
            new SetPlatformServiceKeyActivationCommand("resend", true, Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.False(inactiveResend.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActivation_DeactivatingProvider_NeverChecksForConflicts()
    {
        var activeSendgrid = ExistingKey("sendgrid", isActive: true);
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("sendgrid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeSendgrid);

        var handler = new SetPlatformServiceKeyActivationCommandHandler(
            repo.Object, ProviderRepository());
        var result = await handler.Handle(
            new SetPlatformServiceKeyActivationCommand("sendgrid", false, Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(activeSendgrid.IsActive);
        repo.Verify(r => r.ListActiveForProviderFamilyAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetActivation_ActivatingCloudflareWhileActiveSendgridExists_IsUnaffected()
    {
        var inactiveCloudflare = ExistingKey("cloudflare", isActive: false);
        var activeSendgrid = ExistingKey("sendgrid", isActive: true);
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("cloudflare", It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveCloudflare);
        var handler = new SetPlatformServiceKeyActivationCommandHandler(
            repo.Object, ProviderRepository());
        var result = await handler.Handle(
            new SetPlatformServiceKeyActivationCommand("cloudflare", true, Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(inactiveCloudflare.IsActive);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_ChangesDisplayNameOnly()
    {
        var entity = ExistingKey();
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var handler = new UpdatePlatformServiceKeyCommandHandler(repo.Object);
        var result = await handler.Handle(
            new UpdatePlatformServiceKeyCommand("resend", "Resend (Prod)", Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Resend (Prod)", entity.DisplayName);
        Assert.Equal("OLD-ENCRYPTED", entity.ApiKeyEncrypted); // key untouched
    }

    [Fact]
    public async Task Verify_SuccessStampsLastVerifiedAt_AndNeverReturnsKeyMaterial()
    {
        var entity = ExistingKey();
        entity.LastVerifiedAt = null;

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt("OLD-ENCRYPTED")).Returns("decrypted-plain");

        var checkedAt = DateTimeOffset.UtcNow;
        var verification = new Mock<IPlatformServiceKeyVerificationService>();
        verification.Setup(v => v.VerifyAsync("resend", "decrypted-plain", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformServiceKeyVerificationResult { Success = true, CheckedAt = checkedAt, Message = "ok" });

        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var handler = new VerifyPlatformServiceKeyCommandHandler(repo.Object, encryption.Object, verification.Object);
        var result = await handler.Handle(new VerifyPlatformServiceKeyCommand("resend", Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Success);
        Assert.Equal(checkedAt, entity.LastVerifiedAt);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("decrypted-plain", serialized);
        Assert.DoesNotContain("OLD-ENCRYPTED", serialized);
    }

    [Fact]
    public async Task Verify_FailureDoesNotStampLastVerifiedAt()
    {
        var entity = ExistingKey();
        entity.LastVerifiedAt = null;

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt(It.IsAny<string>())).Returns("decrypted-plain");

        var verification = new Mock<IPlatformServiceKeyVerificationService>();
        verification.Setup(v => v.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlatformServiceKeyVerificationResult { Success = false, CheckedAt = DateTimeOffset.UtcNow, Message = "bad" });

        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var handler = new VerifyPlatformServiceKeyCommandHandler(repo.Object, encryption.Object, verification.Object);
        var result = await handler.Handle(new VerifyPlatformServiceKeyCommand("resend", Actor), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Success);
        Assert.Null(entity.LastVerifiedAt);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_UnknownServiceKey_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);

        var handler = new VerifyPlatformServiceKeyCommandHandler(
            repo.Object, Mock.Of<IEncryptionService>(), Mock.Of<IPlatformServiceKeyVerificationService>());
        var result = await handler.Handle(new VerifyPlatformServiceKeyCommand("resend", Actor), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    // 3. Query / DTO tests

    [Fact]
    public async Task Get_UnknownServiceKey_ReturnsNotFound()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);

        var handler = new GetPlatformServiceKeyQueryHandler(repo.Object);
        var result = await handler.Handle(new GetPlatformServiceKeyQuery("nope"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task ListAndDetail_NeverContainKeyMaterial()
    {
        var entity = ExistingKey();
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey> { entity });
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var listResult = await new ListPlatformServiceKeysQueryHandler(repo.Object)
            .Handle(new ListPlatformServiceKeysQuery(), CancellationToken.None);
        var getResult = await new GetPlatformServiceKeyQueryHandler(repo.Object)
            .Handle(new GetPlatformServiceKeyQuery("resend"), CancellationToken.None);

        Assert.True(listResult.IsSuccess);
        Assert.True(getResult.IsSuccess);
        Assert.Single(listResult.Value!);

        var serialized = System.Text.Json.JsonSerializer.Serialize(listResult.Value)
            + System.Text.Json.JsonSerializer.Serialize(getResult.Value);
        Assert.DoesNotContain("OLD-ENCRYPTED", serialized);
        Assert.DoesNotContain("apiKey", serialized, StringComparison.OrdinalIgnoreCase);
    }

    // 5. Security assertions on response DTO shapes

    [Fact]
    public void Security_ResponseDtos_DoNotExposeKeyMaterial()
    {
        var dtoTypes = new[] { typeof(PlatformServiceKeyDto), typeof(ServiceKeyVerificationResultDto) };
        foreach (var dtoType in dtoTypes)
        {
            var props = dtoType.GetProperties().Select(p => p.Name.ToLowerInvariant()).ToList();
            Assert.DoesNotContain(props, p => p.Contains("apikey"));
            Assert.DoesNotContain(props, p => p.Contains("encrypted"));
            Assert.DoesNotContain(props, p => p.Contains("secret"));
        }
    }

    // Infrastructure stubs (verification + resolver)

    [Fact]
    public async Task VerificationStub_AcceptsNonEmptyKey_RejectsEmptyKey()
    {
        var service = new PlatformServiceKeyVerificationService(
            NullLogger<PlatformServiceKeyVerificationService>.Instance);

        var ok = await service.VerifyAsync("resend", "re_valid_key_123", CancellationToken.None);
        var empty = await service.VerifyAsync("sendgrid", "", CancellationToken.None);

        Assert.True(ok.Success);
        Assert.False(empty.Success);
        Assert.NotEqual(default, ok.CheckedAt);
    }

    [Fact]
    public async Task Resolver_ReturnsDecryptedKeyOnlyForActiveRows()
    {
        var active = ExistingKey("resend", isActive: true);
        var inactive = ExistingKey("sendgrid", isActive: false);

        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt("OLD-ENCRYPTED")).Returns("decrypted-plain");

        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.GetByServiceKeyAsync("resend", It.IsAny<CancellationToken>())).ReturnsAsync(active);
        repo.Setup(r => r.GetByServiceKeyAsync("sendgrid", It.IsAny<CancellationToken>())).ReturnsAsync(inactive);
        repo.Setup(r => r.GetByServiceKeyAsync("cloudflare", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformServiceKey?)null);

        var resolver = new PlatformServiceKeyResolver(repo.Object, encryption.Object);

        Assert.Equal("decrypted-plain", await resolver.ResolveActiveKeyAsync("resend", CancellationToken.None));
        Assert.Null(await resolver.ResolveActiveKeyAsync("sendgrid", CancellationToken.None));
        Assert.Null(await resolver.ResolveActiveKeyAsync("cloudflare", CancellationToken.None));
    }

    [Fact]
    public void Catalog_IsTransactionalEmailProvider_TrueForSendgridAndResend_FalseForCloudflare()
    {
        Assert.True(PlatformServiceKeyCatalog.IsTransactionalEmailProvider("sendgrid"));
        Assert.True(PlatformServiceKeyCatalog.IsTransactionalEmailProvider("resend"));
        Assert.False(PlatformServiceKeyCatalog.IsTransactionalEmailProvider("cloudflare"));
        Assert.False(PlatformServiceKeyCatalog.IsTransactionalEmailProvider("cloudflare_r2"));
        Assert.False(PlatformServiceKeyCatalog.IsTransactionalEmailProvider("mailchimp"));
    }

    [Fact]
    public async Task ResolveActiveTransactionalEmailProvider_ReturnsNotConfigured_WhenZeroActive()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey>());

        var resolver = new PlatformServiceKeyResolver(repo.Object, Mock.Of<IEncryptionService>());
        var result = await resolver.ResolveActiveTransactionalEmailProviderAsync(CancellationToken.None);

        Assert.Equal(TransactionalEmailProviderResolutionStatus.NotConfigured, result.Status);
        Assert.Null(result.ProviderKey);
        Assert.Null(result.ApiKey);
    }

    [Fact]
    public async Task ResolveActiveTransactionalEmailProvider_ReturnsResolvedWithDecryptedKey_WhenOneActive()
    {
        var activeSendgrid = ExistingKey("sendgrid", isActive: true);
        var encryption = new Mock<IEncryptionService>();
        encryption.Setup(e => e.Decrypt("OLD-ENCRYPTED")).Returns("decrypted-plain");

        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey> { activeSendgrid });

        var resolver = new PlatformServiceKeyResolver(repo.Object, encryption.Object);
        var result = await resolver.ResolveActiveTransactionalEmailProviderAsync(CancellationToken.None);

        Assert.Equal(TransactionalEmailProviderResolutionStatus.Resolved, result.Status);
        Assert.Equal("sendgrid", result.ProviderKey);
        Assert.Equal("decrypted-plain", result.ApiKey);
    }

    [Fact]
    public async Task ResolveActiveTransactionalEmailProvider_ReturnsAmbiguous_WhenTwoActive()
    {
        var repo = new Mock<IPlatformServiceKeyRepository>();
        repo.Setup(r => r.ListActiveForProviderFamilyAsync(
                PlatformProviderFamilies.TransactionalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlatformServiceKey>
            {
                ExistingKey("resend", isActive: true),
                ExistingKey("sendgrid", isActive: true)
            });

        var resolver = new PlatformServiceKeyResolver(repo.Object, Mock.Of<IEncryptionService>());
        var result = await resolver.ResolveActiveTransactionalEmailProviderAsync(CancellationToken.None);

        Assert.Equal(TransactionalEmailProviderResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ProviderKey);
        Assert.Null(result.ApiKey);
    }

    // 4. Permission tests

    [Fact]
    public void Authorization_Endpoints_RequireAdminPolicyAndCorrectPlatformPermission()
    {
        var controllerType = typeof(PlatformServiceKeysController);

        var authorizeAttr = controllerType.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorizeAttr);
        Assert.Equal("AdminPolicy", authorizeAttr!.Policy);

        var readEndpoints = new[] { "ListServiceKeys", "GetServiceKey" };
        var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(8, methods.Length);

        foreach (var method in methods)
        {
            var permissionAttr = method.GetCustomAttribute<RequirePlatformPermissionAttribute>();
            Assert.NotNull(permissionAttr);

            if (readEndpoints.Contains(method.Name))
                Assert.Equal(PlatformPermissionCatalog.SystemConfigRead, permissionAttr!.Permission);
            else
                Assert.Equal(PlatformPermissionCatalog.SystemConfigManage, permissionAttr!.Permission);
        }
    }

    private static AuthorizationFilterContext BuildFilterContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public void PermissionFilter_Unauthenticated_LeavesResultForAuthorize401()
    {
        var attr = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.SystemConfigRead);
        var context = BuildFilterContext(new ClaimsPrincipal(new ClaimsIdentity()));

        attr.OnAuthorization(context);

        // The filter defers to [Authorize], which produces the 401.
        Assert.Null(context.Result);
    }

    [Fact]
    public void PermissionFilter_AuthenticatedWithoutPermission_Returns403()
    {
        var attr = new RequirePlatformPermissionAttribute(PlatformPermissionCatalog.SystemConfigManage);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(PlatformPermissionCatalog.PermissionClaimType, PlatformPermissionCatalog.SystemConfigRead)
        }, authenticationType: "test");
        var context = BuildFilterContext(new ClaimsPrincipal(identity));

        attr.OnAuthorization(context);

        var objectResult = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, objectResult.StatusCode);
    }

    [Theory]
    [InlineData(PlatformPermissionCatalog.SystemConfigRead)]
    [InlineData(PlatformPermissionCatalog.SystemConfigManage)]
    public void PermissionFilter_AuthenticatedWithPermission_Passes(string permission)
    {
        var attr = new RequirePlatformPermissionAttribute(permission);
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(PlatformPermissionCatalog.PermissionClaimType, permission)
        }, authenticationType: "test");
        var context = BuildFilterContext(new ClaimsPrincipal(identity));

        attr.OnAuthorization(context);

        Assert.Null(context.Result);
    }
}
