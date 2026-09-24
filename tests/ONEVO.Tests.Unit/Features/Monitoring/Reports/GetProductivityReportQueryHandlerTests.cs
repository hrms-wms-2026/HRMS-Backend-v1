using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Reports;

public class GetProductivityReportQueryHandlerTests
{
    private readonly Mock<IProductivityReportRepository> _reports = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private GetProductivityReportQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetProductivityReportQueryHandler(_reports.Object, _tenantContext.Object);
    }

    private static ProductivityAggregate SampleAggregate(int days = 5) =>
        new(100, 20, 10, 60, 5, 25, 75m, 400, 30, days);

    [Fact]
    public async Task EmployeeScope_ReturnsEmployeeAggregate()
    {
        _reports.Setup(r => r.GetEmployeeAggregateAsync(TenantId, EmployeeId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate());
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetProductivityReportQuery { Scope = ProductivityReportScope.Employee, ScopeId = EmployeeId, From = From, To = To },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DayCount.Should().Be(5);
    }

    [Fact]
    public async Task DepartmentScope_UnknownDepartment_ReturnsNotFound()
    {
        _reports.Setup(r => r.GetDepartmentAggregateAsync(TenantId, DepartmentId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductivityAggregate?)null);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetProductivityReportQuery { Scope = ProductivityReportScope.Department, ScopeId = DepartmentId, From = From, To = To },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task TenantScope_IgnoresScopeId_ReturnsTenantAggregate()
    {
        _reports.Setup(r => r.GetTenantAggregateAsync(TenantId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate(days: 20));
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetProductivityReportQuery { Scope = ProductivityReportScope.Tenant, From = From, To = To },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DayCount.Should().Be(20);
    }

    [Fact]
    public async Task EmployeeOrDepartmentScope_MissingScopeId_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetProductivityReportQuery { Scope = ProductivityReportScope.Employee, ScopeId = null, From = From, To = To },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task FromAfterTo_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetProductivityReportQuery { Scope = ProductivityReportScope.Tenant, From = To, To = From },
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
