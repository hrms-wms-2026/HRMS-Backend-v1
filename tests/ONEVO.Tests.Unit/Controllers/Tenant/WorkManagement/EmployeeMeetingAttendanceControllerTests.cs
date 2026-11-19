using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.WorkManagement;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.WorkManagement;

/// <summary>
/// GET /api/v1/employees/{employeeId}/meeting-attendance (spec D16, D17): the reporting-line attendance view on
/// the employee profile. The controller maps the query and the failures; who may see what is decided in the
/// application layer, so "does not exist" and "not allowed" share one 404.
/// </summary>
public sealed class EmployeeMeetingAttendanceControllerTests
{
    private static readonly Guid Employee = Guid.NewGuid();
    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMediator> _mediator = new();
    private EmployeeMeetingAttendanceController Controller => new(_mediator.Object);

    [Fact]
    public async Task Get_ReturnsTheItems()
    {
        var items = new List<EmployeeMeetingAttendanceItem>
        {
            new(Guid.NewGuid(), From, From.AddMinutes(30), "Weekly sync", "Priya K.", "synced", "Recorded", "joined", 900),
        };
        _mediator.Setup(m => m.Send(It.IsAny<EmployeeMeetingAttendanceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeMeetingAttendanceResponse>.Success(new EmployeeMeetingAttendanceResponse(items)));

        var result = Assert.IsType<OkObjectResult>(await Controller.Get(Employee, From, To, CancellationToken.None));

        Assert.Equal(items, Assert.IsType<EmployeeMeetingAttendanceResponse>(result.Value).Items);
    }

    [Fact]
    public async Task Get_PassesTheEmployeeAndTheWindowThrough()
    {
        EmployeeMeetingAttendanceQuery? sent = null;
        _mediator.Setup(m => m.Send(It.IsAny<EmployeeMeetingAttendanceQuery>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<EmployeeMeetingAttendanceResponse>> q, CancellationToken _) => sent = (EmployeeMeetingAttendanceQuery)q)
            .ReturnsAsync(Result<EmployeeMeetingAttendanceResponse>.Success(new EmployeeMeetingAttendanceResponse([])));

        await Controller.Get(Employee, From, To, CancellationToken.None);

        Assert.Equal((Employee, From, To), (sent!.EmployeeId, sent.From, sent.To));
    }

    [Fact]
    public async Task AnOutsider_Is404WithItsCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<EmployeeMeetingAttendanceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeMeetingAttendanceResponse>.Failure("Meeting not found.", 404, MeetingErrorCodes.NotFound));

        var result = Assert.IsType<ObjectResult>(await Controller.Get(Employee, null, null, CancellationToken.None));

        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal((404, 404, MeetingErrorCodes.NotFound), (result.StatusCode, problem.Status, problem.Extensions["code"]));
    }

    [Fact]
    public async Task AWindowOver92Days_Is400WithItsCode()
    {
        _mediator.Setup(m => m.Send(It.IsAny<EmployeeMeetingAttendanceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<EmployeeMeetingAttendanceResponse>.Failure("The window is too long.", 400, MeetingErrorCodes.InvalidTime));

        var result = Assert.IsType<ObjectResult>(await Controller.Get(Employee, From.AddDays(-93), To, CancellationToken.None));

        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal((400, MeetingErrorCodes.InvalidTime), (result.StatusCode, problem.Extensions["code"]));
    }
}
