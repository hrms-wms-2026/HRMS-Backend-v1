using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Access;
using ONEVO.Application.Features.Monitoring.Screenshots.Queries.GetScreenshots;
using ONEVO.Application.Features.Monitoring.Screenshots.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Screenshots.Entities;

namespace ONEVO.Tests.Unit.Features.Monitoring.Screenshots;

/// <summary>
/// Task 27: monitoring:read alone used to list every employee's screenshots in the tenant.
/// The list now follows management coverage, like the per-employee monitoring endpoints.
/// </summary>
public class GetScreenshotsQueryHandlerTests
{
    private readonly Mock<IEvidenceAssetRepository> _assets = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IEmployeeViewAccess> _viewAccess = new();
    private readonly Guid _tenantId = Guid.NewGuid();

    public GetScreenshotsQueryHandlerTests()
    {
        _tenantContext.Setup(t => t.TenantId).Returns(_tenantId);
    }

    private GetScreenshotsQueryHandler CreateHandler() => new(_assets.Object, _tenantContext.Object, _viewAccess.Object);

    private static (List<MonitoringEvidenceAsset>, int) Page(params MonitoringEvidenceAsset[] items) => (items.ToList(), items.Length);

    private MonitoringEvidenceAsset Asset(Guid employeeId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = _tenantId,
        EmployeeId = employeeId,
        FileRecordId = Guid.NewGuid(),
        CapturedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Handle_FilterOnEmployeeOutsideCoverage_Returns403_WithoutQuerying()
    {
        var employeeId = Guid.NewGuid();
        _viewAccess.Setup(v => v.CanViewAsync(employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateHandler().Handle(new GetScreenshotsQuery(employeeId, null, null, 1, 20), default);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        _assets.Verify(a => a.GetPagedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_FilterOnCoveredEmployee_ReturnsThatEmployeesAssets()
    {
        var employeeId = Guid.NewGuid();
        _viewAccess.Setup(v => v.CanViewAsync(employeeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _assets.Setup(a => a.GetPagedAsync(_tenantId, employeeId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(Asset(employeeId)));

        var result = await CreateHandler().Handle(new GetScreenshotsQuery(employeeId, null, null, 1, 20), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == employeeId);
    }

    [Fact]
    public async Task Handle_NoFilter_ListsOnlyViewableEmployees()
    {
        var covered = Guid.NewGuid();
        IReadOnlyCollection<Guid> viewable = [covered];
        _viewAccess.Setup(v => v.ListViewableEmployeeIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(viewable);
        _assets.Setup(a => a.GetPagedForEmployeesAsync(_tenantId, viewable, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(Asset(covered)));

        var result = await CreateHandler().Handle(new GetScreenshotsQuery(null, null, null, 1, 20), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.EmployeeId == covered);
        _assets.Verify(a => a.GetPagedAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NoFilter_UnrestrictedViewer_ListsTheTenant()
    {
        _viewAccess.Setup(v => v.ListViewableEmployeeIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid>?)null);
        _assets.Setup(a => a.GetPagedAsync(_tenantId, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page(Asset(Guid.NewGuid()), Asset(Guid.NewGuid())));

        var result = await CreateHandler().Handle(new GetScreenshotsQuery(null, null, null, 1, 20), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().HaveCount(2);
    }
}
