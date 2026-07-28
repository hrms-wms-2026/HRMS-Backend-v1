using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Queries.ListTenantConfigurationTemplateApplications;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.ConfigurationTemplates;

public sealed class ListTenantConfigurationTemplateApplicationsQueryHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<ITenantConfigurationTemplateApplicationRepository> _applications = new();
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");

    private ListTenantConfigurationTemplateApplicationsQueryHandler BuildSut() =>
        new(_tenants.Object, _applications.Object);

    [Fact]
    public async Task Handle_TenantMissing_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ListTenantConfigurationTemplateApplicationsQuery(TenantId, 1, 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_TenantExists_ReturnsPagedApplications()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(new Tenant { Id = TenantId });
        _applications.Setup(a => a.ListByTenantAsync(TenantId, 0, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantConfigurationTemplateApplication>());
        _applications.Setup(a => a.CountByTenantAsync(TenantId, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ListTenantConfigurationTemplateApplicationsQuery(TenantId, 1, 25),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
    }
}
