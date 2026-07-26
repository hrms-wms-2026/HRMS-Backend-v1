using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.DeactivateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class DeactivateConfigurationTemplateCommandHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private static readonly Guid TemplateId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private DeactivateConfigurationTemplateCommandHandler BuildSut() => new(_templates.Object, _uow.Object);

    [Fact]
    public async Task Handle_NoActiveReferences_SetsIsActiveFalse()
    {
        var template = new ConfigurationTemplate
        {
            Id = TemplateId,
            TemplateKey = "k",
            TemplateType = ConfigurationTemplate.TypeConfiguration,
            Name = "n",
            IsActive = true,
            ModuleKeysJson = "[]",
            PayloadJson = "{}"
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);

        var sut = BuildSut();
        var result = await sut.Handle(new DeactivateConfigurationTemplateCommand(TemplateId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsActive.Should().BeFalse();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_TemplateMissing_ReturnsNotFound()
    {
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new DeactivateConfigurationTemplateCommand(TemplateId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
