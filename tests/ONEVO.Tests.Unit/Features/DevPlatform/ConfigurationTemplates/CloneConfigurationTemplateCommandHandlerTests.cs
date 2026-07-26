using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CloneConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class CloneConfigurationTemplateCommandHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private static readonly Guid TemplateId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ActorId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private CloneConfigurationTemplateCommandHandler BuildSut() => new(_templates.Object, _uow.Object);

    [Fact]
    public async Task Handle_CloningSystemTemplate_CreatesEditableCopyWithNewKey()
    {
        var source = new ConfigurationTemplate
        {
            Id = TemplateId,
            TemplateKey = "uk-standard-time-off",
            TemplateType = ConfigurationTemplate.TypeTimeOffPolicy,
            Name = "UK Standard Time Off",
            Version = 3,
            IsSystem = true,
            ModuleKeysJson = """["time_off"]""",
            PayloadJson = """{"rules":[]}"""
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(source);
        _templates.Setup(t => t.GetByTemplateKeyAsync("uk-standard-time-off-copy", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new CloneConfigurationTemplateCommand(TemplateId, ActorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TemplateKey.Should().Be("uk-standard-time-off-copy");
        result.Value!.IsSystem.Should().BeFalse();
        result.Value!.Version.Should().Be(1);
    }

    [Fact]
    public async Task Handle_TemplateMissing_ReturnsNotFound()
    {
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new CloneConfigurationTemplateCommand(TemplateId, ActorId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
