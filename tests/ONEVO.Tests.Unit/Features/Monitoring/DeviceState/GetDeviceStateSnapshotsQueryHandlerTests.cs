using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.DeviceState.Queries.GetDeviceStateSnapshots;
using ONEVO.Application.Features.Monitoring.DeviceState.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.DeviceState.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.DeviceState;

public class GetDeviceStateSnapshotsQueryHandlerTests
{
    private readonly Mock<IDeviceStateSnapshotRepository> _snapshots = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    private GetDeviceStateSnapshotsQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetDeviceStateSnapshotsQueryHandler(_snapshots.Object, _tenantContext.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedMappedResults()
    {
        _snapshots.Setup(r => r.GetTotalCountAsync(TenantId, EmployeeId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _snapshots.Setup(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeviceStateSnapshot
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = DateTimeOffset.UtcNow, IdleSeconds = 130, IsIdle = true
            }]);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetDeviceStateSnapshotsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.IsIdle && i.IdleSeconds == 130);
    }

    [Fact]
    public async Task Handle_MissingEmployeeId_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetDeviceStateSnapshotsQuery { EmployeeId = Guid.Empty, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_NoTenantContext_ReturnsUnauthorized()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(Guid.Empty);
        var sut = new GetDeviceStateSnapshotsQueryHandler(_snapshots.Object, _tenantContext.Object);

        var result = await sut.Handle(
            new GetDeviceStateSnapshotsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }
}
