using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Requests;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Cancellation.Commands;
using ONEVO.Application.Features.Leave.Cancellation.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Cancellation;

public class LeaveCancellationControllerTests
{
    [Fact]
    public async Task Cancel_SendsCommandWithRouteAndBody()
    {
        var requestId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<CancelLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CancelLeaveRequestResponse>.Success(new CancelLeaveRequestResponse(
                requestId, "cancelled", false, null, 1m, 0m, 12m, "coverage", DateTimeOffset.UtcNow)));
        var controller = new LeaveRequestsController(mediator.Object);

        var response = await controller.Cancel(
            requestId,
            new CancelLeaveRequestRequest("coverage", new DateOnly(2026, 8, 24), "42"),
            CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<CancelLeaveRequestCommand>(c =>
            c.RequestId == requestId &&
            c.Reason == "coverage" &&
            c.EffectiveDate == new DateOnly(2026, 8, 24) &&
            c.ExpectedVersion == "42"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_ReturnsProblemOnFailure()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<CancelLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CancelLeaveRequestResponse>.Conflict("This leave request has already been cancelled"));
        var controller = new LeaveRequestsController(mediator.Object);

        var response = await controller.Cancel(Guid.NewGuid(), new CancelLeaveRequestRequest(null, null, null), CancellationToken.None);

        var problem = response.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }
}
