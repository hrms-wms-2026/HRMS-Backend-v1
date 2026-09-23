using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Calendar.DTOs.Responses;
using ONEVO.Application.Features.Leave.Calendar.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Calendar;

public class LeaveCalendarControllerTests
{
    [Fact]
    public async Task Get_SendsCalendarQuery()
    {
        var departmentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetLeaveCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveCalendarMonthResponse>.Success(new LeaveCalendarMonthResponse(
                2026,
                8,
                new DateOnly(2026, 8, 1),
                new DateOnly(2026, 8, 31),
                true,
                true,
                [])));

        var controller = new LeaveCalendarController(mediator.Object);
        var result = await controller.Get(2026, 8, departmentId, false, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(
            It.Is<GetLeaveCalendarQuery>(q =>
                q.Year == 2026 &&
                q.Month == 8 &&
                q.DepartmentId == departmentId &&
                q.IncludeTentative == false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ReturnsProblem_WhenQueryFails()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<GetLeaveCalendarQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveCalendarMonthResponse>.Forbidden("Nope"));

        var controller = new LeaveCalendarController(mediator.Object);
        var result = await controller.Get(2026, 8, null, null, CancellationToken.None);

        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(403);
    }
}
