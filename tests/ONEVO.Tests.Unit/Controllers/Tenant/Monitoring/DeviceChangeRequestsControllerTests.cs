using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Monitoring.DeviceChangeRequests;
using ONEVO.Api.Controllers.Tenant.Monitoring;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.TrayActivation.Commands.DeviceChangeRequests;
using ONEVO.Application.Features.Monitoring.TrayActivation.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.TrayActivation.Queries.DeviceChangeRequests;
using Xunit;

namespace ONEVO.Tests.Unit.Controllers.Tenant.Monitoring;

public sealed class DeviceChangeRequestsControllerTests
{
    private static readonly Guid RequestId = Guid.NewGuid();

    [Fact]
    public async Task Approvals_MapsQueryAndReturnsPagedResult()
    {
        var mediator = new Mock<IMediator>();
        var expected = new PagedResult<DeviceChangeRequestResponse>(Array.Empty<DeviceChangeRequestResponse>(), 1, 20, 0);
        mediator.Setup(m => m.Send(It.IsAny<ListDeviceChangeRequestApprovalsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<DeviceChangeRequestResponse>>.Success(expected));
        var controller = new DeviceChangeRequestsController(mediator.Object);

        var result = await controller.Approvals(new PagedRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
    }

    [Fact]
    public async Task Approve_SendsCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = Response();
        mediator.Setup(m => m.Send(It.IsAny<ApproveDeviceChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DeviceChangeRequestResponse>.Success(expected));
        var controller = new DeviceChangeRequestsController(mediator.Object);

        var result = await controller.Approve(RequestId, new ReviewDeviceChangeRequestRequest(null), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(m => m.Send(
            It.Is<ApproveDeviceChangeRequestCommand>(c => c.Id == RequestId && c.ReviewComment == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_WhenNoCommentProvided_ReturnsFailureStatusCode()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RejectDeviceChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DeviceChangeRequestResponse>.Failure("A review comment is required when rejecting a request."));
        var controller = new DeviceChangeRequestsController(mediator.Object);

        var result = await controller.Reject(RequestId, new ReviewDeviceChangeRequestRequest(null), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    private static DeviceChangeRequestResponse Response() => new(
        RequestId, Guid.NewGuid(), "Alex Employee", "New PC", "Windows",
        "approved", DateTimeOffset.UtcNow, Guid.NewGuid(), DateTimeOffset.UtcNow, null);
}
