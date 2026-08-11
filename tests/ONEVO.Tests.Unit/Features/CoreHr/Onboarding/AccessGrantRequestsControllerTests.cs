using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.CoreHr.AccessGrantRequests;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;

namespace ONEVO.Tests.Unit.Features.CoreHr.Onboarding;

public sealed class AccessGrantRequestsControllerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly AccessGrantRequestsController _sut;

    public AccessGrantRequestsControllerTests()
    {
        _sut = new AccessGrantRequestsController(_mediator.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task ApproveAndSendInvite_SendsCommandWithRouteId()
    {
        var id = Guid.NewGuid();
        var response = new ApproveAccessGrantRequestResponse(id, Guid.NewGuid(), Guid.NewGuid(), "finalized", true, 3, "Approved", "onboarding.access_grant.approved");
        _mediator
            .Setup(m => m.Send(It.IsAny<ApproveAccessGrantRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApproveAccessGrantRequestResponse>.Success(response));

        var result = await _sut.ApproveAndSendInvite(id, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<ApproveAccessGrantRequestCommand>(c => c.AccessGrantRequestId == id),
            It.IsAny<CancellationToken>()), Times.Once);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task ApproveAndSendInvite_ReturnsProblemOnFailure()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<ApproveAccessGrantRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApproveAccessGrantRequestResponse>.Conflict("already decided"));

        var result = await _sut.ApproveAndSendInvite(id, CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task Reject_SendsCommandWithRouteIdAndNotePayload()
    {
        var id = Guid.NewGuid();
        var response = new RejectAccessGrantRequestResponse(id, Guid.NewGuid(), "Rejected", "waiting_for_position_approval", "waiting_for_position_approval", "onboarding.access_grant.rejected");
        _mediator
            .Setup(m => m.Send(It.IsAny<RejectAccessGrantRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RejectAccessGrantRequestResponse>.Success(response));

        var result = await _sut.Reject(id, new RejectAccessGrantRequestRequest("Position filled internally"), CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RejectAccessGrantRequestCommand>(c => c.AccessGrantRequestId == id && c.DecisionNote == "Position filled internally"),
            It.IsAny<CancellationToken>()), Times.Once);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task Reject_AllowsNullBody()
    {
        var id = Guid.NewGuid();
        var response = new RejectAccessGrantRequestResponse(id, Guid.NewGuid(), "Rejected", "waiting_for_position_approval", "waiting_for_position_approval", "onboarding.access_grant.rejected");
        _mediator
            .Setup(m => m.Send(It.IsAny<RejectAccessGrantRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RejectAccessGrantRequestResponse>.Success(response));

        await _sut.Reject(id, null, CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<RejectAccessGrantRequestCommand>(c => c.AccessGrantRequestId == id && c.DecisionNote == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_ReturnsProblemOnFailure()
    {
        var id = Guid.NewGuid();
        _mediator
            .Setup(m => m.Send(It.IsAny<RejectAccessGrantRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RejectAccessGrantRequestResponse>.NotFound("not found"));

        var result = await _sut.Reject(id, null, CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }
}
