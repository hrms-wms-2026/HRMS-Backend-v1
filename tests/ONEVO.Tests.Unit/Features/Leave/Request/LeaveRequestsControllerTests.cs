using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Requests;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Request.Commands.SubmitLeaveRequest;
using ONEVO.Application.Features.Leave.Request.DTOs.Responses;
using ONEVO.Application.Features.Leave.Request.Queries.ListMyLeaveRequests;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Request;

public class LeaveRequestsControllerTests
{
    [Fact]
    public async Task Submit_SendsOwnSubmitCommand()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<SubmitLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveRequestResponse>.Success(SampleResponse()));
        var controller = new LeaveRequestsController(mediator.Object);

        var response = await controller.Submit(new SubmitLeaveRequestRequest(
            Guid.NewGuid(), new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18), null, null, null), CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<SubmitLeaveRequestCommand>(c => !c.IsOnBehalfRequest), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListMine_SendsOwnListQuery()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<ListMyLeaveRequestsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LeaveRequestListItemResponse>>.Success([]));
        var controller = new LeaveRequestsController(mediator.Object);

        var response = await controller.ListMine(null, null, null, null, CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
    }

    private static LeaveRequestResponse SampleResponse() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Annual Leave", "AL",
        new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 18), null, 1m, 1m, 0m, "pending", false, null,
        new LeaveRequestBalanceImpactResponse(10m, 1m, 9m), [], new LeaveRequestConflictSnapshotResponse([], [], null),
        DateTimeOffset.UtcNow);
}
