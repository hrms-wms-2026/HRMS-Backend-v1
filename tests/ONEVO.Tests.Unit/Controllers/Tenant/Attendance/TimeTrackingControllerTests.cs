using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Attendance.TimeTracking;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockIn;
using ONEVO.Application.Features.TimeAttendance.Commands.ClockOut;
using ONEVO.Application.Features.TimeAttendance.Commands.EndBreak;
using ONEVO.Application.Features.TimeAttendance.Commands.StartBreak;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries;
using MediatR;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.Attendance;

public sealed class TimeTrackingControllerTests
{
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();
    private static readonly DateOnly WorkDate = new(2026, 8, 21);

    [Fact]
    public async Task ClockIn_SendsSourceOnlyCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = SampleResponse();
        mediator
            .Setup(x => x.Send(It.IsAny<ClockInCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(expected));
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.ClockIn(new ClockInRequest("web"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.Is<ClockInCommand>(command => command.Source == "web"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ClockOut_SendsSelfServiceCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = SampleResponse();
        mediator
            .Setup(x => x.Send(It.IsAny<ClockOutCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(expected));
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.ClockOut(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.IsAny<ClockOutCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StartBreak_SendsSelfServiceCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = SampleResponse();
        mediator
            .Setup(x => x.Send(It.IsAny<StartBreakCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(expected));
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.StartBreak(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.IsAny<StartBreakCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EndBreak_SendsSelfServiceCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = SampleResponse();
        mediator
            .Setup(x => x.Send(It.IsAny<EndBreakCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceTodayResponse>.Success(expected));
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.EndBreak(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.IsAny<EndBreakCommand>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HistoryDetail_SendsQueryWithEmployeeIdAndDateAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = new AttendanceDayDetailResponse(
            new AttendanceHistoryRow(
                Guid.NewGuid(), WorkDate, null, null, null, false, 0, 0, null, null, "present",
                true, false, false, false),
            Array.Empty<TimelineEvent>(),
            null);
        mediator
            .Setup(x => x.Send(It.IsAny<GetAttendanceDayDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceDayDetailResponse>.Success(expected));
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.HistoryDetail(EmployeeId, WorkDate, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.Is<GetAttendanceDayDetailQuery>(q => q.EmployeeId == EmployeeId && q.Date == WorkDate),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HistoryDetail_ForbiddenResult_ReturnsProblemWith403()
    {
        var mediator = new Mock<IMediator>();
        mediator
            .Setup(x => x.Send(It.IsAny<GetAttendanceDayDetailQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AttendanceDayDetailResponse>.Forbidden());
        var controller = new TimeTrackingController(mediator.Object);

        var result = await controller.HistoryDetail(EmployeeId, WorkDate, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public void RequestContracts_DoNotExposeTenantOrEmployeeIdentifiers()
    {
        var forbidden = new[]
        {
            "TenantId", "EmployeeId", "LegalEntityId", "WorkDate", "ClockInAt", "ClockOutAt",
            "BreakStart", "BreakEnd", "Duration", "ClientTimestamp"
        };
        var properties = typeof(ClockInRequest).GetProperties()
            .Concat(typeof(ClockOutRequest).GetProperties())
            .Concat(typeof(StartBreakRequest).GetProperties())
            .Concat(typeof(EndBreakRequest).GetProperties())
            .Select(property => property.Name);

        Assert.DoesNotContain(properties, property => forbidden.Contains(property, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void BreakRequestContracts_AreEmpty()
    {
        Assert.Empty(typeof(StartBreakRequest).GetProperties());
        Assert.Empty(typeof(EndBreakRequest).GetProperties());
    }

    private static AttendanceTodayResponse SampleResponse()
        => new(
            EmployeeId,
            LegalEntityId,
            WorkDate,
            "Asia/Colombo",
            "configured",
            "configured",
            true,
            false,
            null,
            "09:00",
            "17:30",
            510,
            60,
            0,
            60,
            "ended",
            "remote",
            "clocked_out",
            null,
            null,
            0,
            "web",
            false,
            false,
            false,
            false,
            false,
            false,
            new AllowedClockInMethods(true, false, false, false, false, null),
            []);
}
