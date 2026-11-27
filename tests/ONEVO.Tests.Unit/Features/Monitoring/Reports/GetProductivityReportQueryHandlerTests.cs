using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Authorization;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.Monitoring.Reports.Queries.GetProductivityReport;
using ONEVO.Application.Features.Monitoring.Reports.RepositoryInterfaces;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Reports;

public class GetProductivityReportQueryHandlerTests
{
    private readonly Mock<IProductivityReportRepository> _reports = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<IEmployeeViewAccess> _viewAccess = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid DepartmentId = Guid.NewGuid();
    private static readonly DateOnly From = new(2026, 8, 1);
    private static readonly DateOnly To = new(2026, 8, 31);

    private static readonly Guid CoveredId = Guid.NewGuid();
    private static readonly Guid OtherId = Guid.NewGuid();
    private static readonly IReadOnlyCollection<Guid> Viewable = [CoveredId];

    /// <summary>Default caller: HR / admin (org:manage), the tenant-wide view.</summary>
    public GetProductivityReportQueryHandlerTests() => AsTenantWideViewer();

    private void AsTenantWideViewer() =>
        _currentUser.Setup(u => u.HasPermission(PermissionCatalog.Org.Manage)).Returns(true);

    /// <summary>A manager without org:manage who covers only <see cref="CoveredId"/>.</summary>
    private void AsCoveringManager()
    {
        _currentUser.Setup(u => u.HasPermission(PermissionCatalog.Org.Manage)).Returns(false);
        _viewAccess.Setup(v => v.ListViewableEmployeeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Viewable);
        _viewAccess.Setup(v => v.CanViewAsync(CoveredId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _viewAccess.Setup(v => v.CanViewAsync(OtherId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private GetProductivityReportQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetProductivityReportQueryHandler(
            _reports.Object, _tenantContext.Object, _currentUser.Object, _viewAccess.Object);
    }

    private static GetProductivityReportQuery Query(ProductivityReportScope scope, Guid? scopeId = null) =>
        new() { Scope = scope, ScopeId = scopeId, From = From, To = To };

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

    // ── Coverage (product decision 2026-11-23: "coverage only") ──

    [Fact]
    public async Task TenantWideViewer_TenantScope_NeverAsksForCoverage()
    {
        _reports.Setup(r => r.GetTenantAggregateAsync(TenantId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate(days: 20));

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Tenant), CancellationToken.None);

        result.Value!.DayCount.Should().Be(20);
        _viewAccess.Verify(v => v.ListViewableEmployeeIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_TenantScope_SumsOnlyCoveredEmployees()
    {
        AsCoveringManager();
        _reports.Setup(r => r.GetEmployeesAggregateAsync(TenantId, Viewable, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate(days: 3));

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Tenant), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DayCount.Should().Be(3);
        _reports.Verify(r => r.GetTenantAggregateAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_DepartmentScope_SumsOnlyCoveredEmployeesOfThatDepartment()
    {
        AsCoveringManager();
        _reports.Setup(r => r.GetDepartmentAggregateForEmployeesAsync(
                TenantId, DepartmentId, Viewable, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate(days: 2));

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Department, DepartmentId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DayCount.Should().Be(2);
        _reports.Verify(r => r.GetDepartmentAggregateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_DepartmentScope_UnknownDepartment_ReturnsNotFound()
    {
        AsCoveringManager();
        _reports.Setup(r => r.GetDepartmentAggregateForEmployeesAsync(
                TenantId, DepartmentId, Viewable, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductivityAggregate?)null);

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Department, DepartmentId), CancellationToken.None);

        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Manager_EmployeeScope_CoveredEmployee_ReturnsTheirAggregate()
    {
        AsCoveringManager();
        _reports.Setup(r => r.GetEmployeeAggregateAsync(TenantId, CoveredId, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SampleAggregate(days: 4));

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Employee, CoveredId), CancellationToken.None);

        result.Value!.DayCount.Should().Be(4);
    }

    [Fact]
    public async Task Manager_EmployeeScope_EmployeeOutsideCoverage_Returns403WithoutQuerying()
    {
        AsCoveringManager();

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Employee, OtherId), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _reports.Verify(r => r.GetEmployeeAggregateAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Manager_CoveringNobody_TenantScope_SumsAnEmptySet()
    {
        AsCoveringManager();
        IReadOnlyCollection<Guid> none = [];
        _viewAccess.Setup(v => v.ListViewableEmployeeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(none);
        _reports.Setup(r => r.GetEmployeesAggregateAsync(TenantId, none, From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProductivityAggregate(0, 0, 0, 0, 0, 0, 0m, 0, 0, 0));

        var result = await BuildSut().Handle(Query(ProductivityReportScope.Tenant), CancellationToken.None);

        result.Value!.TotalActiveMinutes.Should().Be(0);
    }
}
