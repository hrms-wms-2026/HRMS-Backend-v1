using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.GetConfigurationTemplateDetail;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class GetConfigurationTemplateDetailQueryHandlerTests
{
    private readonly Mock<IConfigurationTemplateRepository> _templates = new();
    private readonly Mock<ITenantConfigurationTemplateApplicationRepository> _applications = new();

    private static readonly Guid TemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private GetConfigurationTemplateDetailQueryHandler BuildSut() => new(_templates.Object, _applications.Object);

    [Fact]
    public async Task Handle_TemplateMissing_ReturnsNotFound()
    {
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConfigurationTemplate?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new GetConfigurationTemplateDetailQuery(TemplateId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_ReturnsTemplateWithApplyHistory()
    {
        var template = new ConfigurationTemplate
        {
            Id = TemplateId,
            TemplateKey = "uk-office-defaults",
            TemplateType = ConfigurationTemplate.TypeConfiguration,
            Name = "UK Office Defaults",
            ModuleKeysJson = "[]",
            PayloadJson = "{}"
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>())).ReturnsAsync(template);
        _applications.Setup(a => a.ListByTemplateAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantConfigurationTemplateApplication>());

        var sut = BuildSut();
        var result = await sut.Handle(new GetConfigurationTemplateDetailQuery(TemplateId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Template.Id.Should().Be(TemplateId);
        result.Value!.ApplyHistory.Should().BeEmpty();
    }
}
