using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.AppUsage.Queries.GetAppUsageSnapshots;
using ONEVO.Application.Features.Monitoring.AppUsage.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.AppUsage.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.AppUsage;

public class GetAppUsageSnapshotsQueryHandlerTests
{
    private readonly Mock<IAppUsageSnapshotRepository> _snapshots = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    private GetAppUsageSnapshotsQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetAppUsageSnapshotsQueryHandler(_snapshots.Object, _tenantContext.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedMappedResults()
    {
        _snapshots.Setup(r => r.GetTotalCountAsync(TenantId, EmployeeId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _snapshots.Setup(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AppUsageSnapshot
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = DateTimeOffset.UtcNow, ProcessName = "code.exe", WindowTitleHash = "abc"
            }]);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetAppUsageSnapshotsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.ProcessName == "code.exe");
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MissingEmployeeId_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetAppUsageSnapshotsQuery { EmployeeId = Guid.Empty, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NoTenantContext_ReturnsUnauthorized()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(Guid.Empty);
        var sut = new GetAppUsageSnapshotsQueryHandler(_snapshots.Object, _tenantContext.Object);

        var result = await sut.Handle(
            new GetAppUsageSnapshotsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task Handle_PageSizeOutOfRange_ClampsTo100()
    {
        _snapshots.Setup(r => r.GetTotalCountAsync(TenantId, EmployeeId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _snapshots.Setup(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetAppUsageSnapshotsQuery { EmployeeId = EmployeeId, Date = Day, PageSize = 5000 },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _snapshots.Verify(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }
}
