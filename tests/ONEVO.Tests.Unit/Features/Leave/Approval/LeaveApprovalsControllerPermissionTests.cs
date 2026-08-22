using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Approvals;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using ONEVO.Application.Features.Leave.Approval.Queries;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class LeaveApprovalsControllerPermissionTests
{
    [Fact]
    public async Task PendingApprovals_SendsPendingQuery()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<ListPendingLeaveApprovalsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LeavePendingApprovalListItemResponse>>.Success([]));
        var controller = new LeaveApprovalsController(mediator.Object);

        var response = await controller.PendingApprovals(null, null, null, null, null, CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.IsAny<ListPendingLeaveApprovalsQuery>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_SendsApproveCommand()
    {
        var requestId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<ApproveLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Success(
                new LeaveApprovalDecisionResponse(requestId, "approved", "approved", 1m, 0m, 12m, [])));
        var controller = new LeaveApprovalsController(mediator.Object);

        var response = await controller.Approve(requestId, new ApproveLeaveRequestRequest("ok"), CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(x => x.Send(It.Is<ApproveLeaveRequestCommand>(c => c.RequestId == requestId && c.Comment == "ok"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RespondInfo_SendsRespondCommandWithEmptyFilesWhenOmitted()
    {
        var requestId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<RespondLeaveInformationCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Success(
                new LeaveApprovalDecisionResponse(requestId, "pending", "pending", 0m, 0m, 12m, [])));
        var controller = new LeaveApprovalsController(mediator.Object);

        var response = await controller.RespondInfo(
            requestId,
            new RespondLeaveInformationRequest("Attached", null),
            CancellationToken.None);

        response.Should().BeOfType<OkObjectResult>();
        mediator.Verify(
            x => x.Send(It.Is<RespondLeaveInformationCommand>(c => c.RequestId == requestId && c.FileRecordIds.Count == 0), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
