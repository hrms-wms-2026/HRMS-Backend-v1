using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class UpdateConfigurationTemplateMetadataCommandHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private static readonly Guid TemplateId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private UpdateConfigurationTemplateMetadataCommandHandler BuildSut() => new(_templates.Object, _uow.Object);

    [Fact]
    public async Task Handle_EditingDescription_IncrementsVersionAndSetsUpdatedAt()
    {
        var template = new ConfigurationTemplate
        {
            Id = TemplateId,
            TemplateKey = "uk-office-defaults",
            TemplateType = ConfigurationTemplate.TypeConfiguration,
            Name = "UK Office Defaults",
            Version = 1,
            IsSystem = false,
            ModuleKeysJson = "[]",
            PayloadJson = "{}"
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateConfigurationTemplateMetadataCommand(TemplateId, null, "New description", null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Version.Should().Be(2);
        result.Value!.UpdatedAt.Should().NotBeNull();
        result.Value!.Description.Should().Be("New description");
    }

    [Fact]
    public async Task Handle_SystemTemplate_ReturnsBadRequest()
    {
        var template = new ConfigurationTemplate
        {
            Id = TemplateId,
            TemplateKey = "system-default",
            TemplateType = ConfigurationTemplate.TypeConfiguration,
            Name = "System Default",
            IsSystem = true,
            ModuleKeysJson = "[]",
            PayloadJson = "{}"
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateConfigurationTemplateMetadataCommand(TemplateId, "New Name", null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_TemplateMissing_ReturnsNotFound()
    {
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new UpdateConfigurationTemplateMetadataCommand(TemplateId, "New Name", null, null, null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
