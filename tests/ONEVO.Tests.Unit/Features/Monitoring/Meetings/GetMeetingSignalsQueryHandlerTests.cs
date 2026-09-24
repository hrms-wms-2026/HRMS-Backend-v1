using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Meetings.Queries.GetMeetingSignals;
using ONEVO.Application.Features.Monitoring.Meetings.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Meetings.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Monitoring.Meetings;

public class GetMeetingSignalsQueryHandlerTests
{
    private readonly Mock<IMeetingSignalRepository> _signals = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 8, 17);

    private GetMeetingSignalsQueryHandler BuildSut()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(TenantId);
        return new GetMeetingSignalsQueryHandler(_signals.Object, _tenantContext.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedMappedResults()
    {
        _signals.Setup(r => r.GetTotalCountAsync(TenantId, EmployeeId, Day, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _signals.Setup(r => r.GetByEmployeeDateAsync(TenantId, EmployeeId, Day, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MeetingSignal
            {
                Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, AgentDeviceId = Guid.NewGuid(),
                CapturedAt = DateTimeOffset.UtcNow, IsMeetingAppRunning = true, ProcessName = "zoom.exe"
            }]);
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetMeetingSignalsQuery { EmployeeId = EmployeeId, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(i => i.ProcessName == "zoom.exe" && i.IsMeetingAppRunning);
    }

    [Fact]
    public async Task Handle_MissingEmployeeId_ReturnsBadRequest()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new GetMeetingSignalsQuery { EmployeeId = Guid.Empty, Date = Day }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
