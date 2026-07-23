using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListConfigurationTemplates;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class ListConfigurationTemplatesQueryHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();

    private ListConfigurationTemplatesQueryHandler BuildSut() => new(_templates.Object);

    [Fact]
    public async Task Handle_DefaultsPageSize_WhenNotProvided()
    {
        _templates.Setup(t => t.ListAsync(null, null, null, 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate>());
        _templates.Setup(t => t.CountAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ListConfigurationTemplatesQuery(null, null, null, 0, 0),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Page.Should().Be(1);
        result.Value!.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task Handle_PassesFiltersThrough_ToRepository()
    {
        _templates.Setup(t => t.ListAsync("time_off_policy", true, "office_it", 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConfigurationTemplate>());
        _templates.Setup(t => t.CountAsync("time_off_policy", true, "office_it", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ListConfigurationTemplatesQuery("time_off_policy", true, "office_it", 1, 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _templates.VerifyAll();
    }
}
