using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.ApplyConfigurationTemplateToTenant;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class ApplyConfigurationTemplateToTenantCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<ITenantConfigurationTemplateApplicationRepository> _applications = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid TemplateId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ActorId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private ApplyConfigurationTemplateToTenantCommandHandler BuildSut() =>
        new(_tenants.Object, _templates.Object, _applications.Object, _entitlements.Object, _uow.Object);

    private static ConfigurationTemplate ActiveTemplate(string type) => new()
    {
        Id = TemplateId,
        TemplateKey = "k",
        TemplateType = type,
        Name = "n",
        Version = 4,
        IsActive = true,
        ModuleKeysJson = "[]",
        PayloadJson = """{"timezone":"Europe/London"}"""
    };

    [Fact]
    public async Task Handle_TenantMissing_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _templates.Verify(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_TemplateMissing_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_InactiveTemplate_ReturnsBadRequest_NoRowWritten()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        var template = ActiveTemplate(ConfigurationTemplate.TypeConfiguration);
        template.IsActive = false;
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        _applications.Verify(a => a.AddAsync(It.IsAny<TenantConfigurationTemplateApplication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ConfigurationType_NeverChecksModuleEntitlement_AlwaysAllowed()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveTemplate(ConfigurationTemplate.TypeConfiguration));

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _entitlements.Verify(
            e => e.IsModuleEnabledAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PositionTemplateType_ModuleNotEntitled_ReturnsBadRequest_NoRowWritten()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveTemplate(ConfigurationTemplate.TypePositionTemplate));
        _entitlements.Setup(e => e.IsModuleEnabledAsync(TenantId, "core_hr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("core_hr");
        _applications.Verify(a => a.AddAsync(It.IsAny<TenantConfigurationTemplateApplication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_HappyPath_WritesImmutableApplicationRow_SnapshottingTemplateState()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        var template = ActiveTemplate(ConfigurationTemplate.TypeConfiguration);
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        TenantConfigurationTemplateApplication? captured = null;
        _applications
            .Setup(a => a.AddAsync(It.IsAny<TenantConfigurationTemplateApplication>(), It.IsAny<CancellationToken>()))
            .Callback<TenantConfigurationTemplateApplication, CancellationToken>((a, _) => captured = a)
            .Returns(Task.CompletedTask);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AppliedVersion.Should().Be(4);
        captured.Should().NotBeNull();
        captured!.TenantId.Should().Be(TenantId);
        captured.ConfigurationTemplateId.Should().Be(TemplateId);
        captured.AppliedVersion.Should().Be(4);
        captured.AppliedPayloadJson.Should().Be(template.PayloadJson);
        captured.Status.Should().Be(TenantConfigurationTemplateApplication.StatusApplied);
        captured.AppliedById.Should().Be(ActorId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ReapplyingSameTemplate_CreatesSecondRow_DoesNotMutateFirst()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        var template = ActiveTemplate(ConfigurationTemplate.TypeConfiguration);
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var sut = BuildSut();
        var first = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);
        var second = await sut.Handle(
            new ApplyConfigurationTemplateToTenantCommand(TenantId, TemplateId, false, ActorId),
            CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value!.ApplicationId.Should().NotBe(second.Value!.ApplicationId);
        _applications.Verify(a => a.AddAsync(It.IsAny<TenantConfigurationTemplateApplication>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
