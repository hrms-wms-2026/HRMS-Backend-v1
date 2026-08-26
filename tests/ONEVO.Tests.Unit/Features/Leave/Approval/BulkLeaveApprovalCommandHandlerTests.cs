using FluentAssertions;
using MediatR;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Approval.Commands;
using ONEVO.Application.Features.Leave.Approval.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Approval;

public class BulkLeaveApprovalCommandHandlerTests
{
    [Fact]
    public async Task BulkApprove_ReturnsPartialSuccessWhenOneRequestFails()
    {
        var okId = Guid.NewGuid();
        var failId = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.Is<ApproveLeaveRequestCommand>(c => c.RequestId == okId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Success(Decision(okId, "approved")));
        mediator.Setup(x => x.Send(It.Is<ApproveLeaveRequestCommand>(c => c.RequestId == failId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeaveApprovalDecisionResponse>.Conflict("This request has already been approved or rejected"));

        var handler = new BulkApproveLeaveRequestsCommandHandler(mediator.Object);
        var result = await handler.Handle(new BulkApproveLeaveRequestsCommand([okId, failId], "ok"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuccessCount.Should().Be(1);
        result.Value.FailureCount.Should().Be(1);
        result.Value.Items.Should().Contain(x => x.RequestId == okId && x.Success);
        result.Value.Items.Should().Contain(x => x.RequestId == failId && !x.Success);
    }

    [Fact]
    public async Task BulkReject_DelegatesEachIdToRejectCommand()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<RejectLeaveRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RejectLeaveRequestCommand command, CancellationToken _) =>
                Result<LeaveApprovalDecisionResponse>.Success(Decision(command.RequestId, "rejected")));

        var handler = new BulkRejectLeaveRequestsCommandHandler(mediator.Object);
        var result = await handler.Handle(new BulkRejectLeaveRequestsCommand([first, second], "coverage"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuccessCount.Should().Be(2);
        mediator.Verify(x => x.Send(It.Is<RejectLeaveRequestCommand>(c => c.RequestId == first && c.Reason == "coverage"), It.IsAny<CancellationToken>()), Times.Once);
        mediator.Verify(x => x.Send(It.Is<RejectLeaveRequestCommand>(c => c.RequestId == second && c.Reason == "coverage"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static LeaveApprovalDecisionResponse Decision(Guid requestId, string status) =>
        new(requestId, status, status, 0m, 0m, 0m, []);
}
