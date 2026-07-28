using System.Text.Json;
using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class CreateConfigurationTemplateCommandHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private CreateConfigurationTemplateCommandHandler BuildSut() => new(_templates.Object, _uow.Object);

    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Handle_HappyPath_CreatesActiveVersionOneNonSystemTemplate()
    {
        _templates.Setup(t => t.GetByTemplateKeyAsync("uk-office-defaults", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateConfigurationTemplateCommand(
                "uk-office-defaults",
                ConfigurationTemplate.TypeConfiguration,
                "UK Office Defaults",
                null,
                new List<string>(),
                null,
                Payload("""{"timezone":"Europe/London"}"""),
                false,
                ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Version.Should().Be(1);
        result.Value!.IsActive.Should().BeTrue();
        result.Value!.IsSystem.Should().BeFalse();
        _templates.Verify(t => t.AddAsync(It.IsAny<ConfigurationTemplate>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateTemplateKey_ReturnsConflict()
    {
        _templates.Setup(t => t.GetByTemplateKeyAsync("uk-office-defaults", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfigurationTemplate { TemplateKey = "uk-office-defaults" });

        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateConfigurationTemplateCommand(
                "uk-office-defaults",
                ConfigurationTemplate.TypeConfiguration,
                "UK Office Defaults",
                null,
                new List<string>(),
                null,
                Payload("{}"),
                false,
                ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        _templates.Verify(t => t.AddAsync(It.IsAny<ConfigurationTemplate>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownTemplateType_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateConfigurationTemplateCommand(
                "some-key",
                "not_a_real_type",
                "Name",
                null,
                new List<string>(),
                null,
                Payload("{}"),
                false,
                ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_PayloadNotAJsonObject_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateConfigurationTemplateCommand(
                "some-key",
                ConfigurationTemplate.TypeConfiguration,
                "Name",
                null,
                new List<string>(),
                null,
                Payload("[1,2,3]"),
                false,
                ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_RequestingIsSystemTrue_IsIgnored_CreatedTemplateIsNeverSystem()
    {
        _templates.Setup(t => t.GetByTemplateKeyAsync("uk-office-defaults", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new CreateConfigurationTemplateCommand(
                "uk-office-defaults",
                ConfigurationTemplate.TypeConfiguration,
                "UK Office Defaults",
                null,
                new List<string>(),
                null,
                Payload("{}"),
                true,
                ActorId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsSystem.Should().BeFalse();
    }
}
