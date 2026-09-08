using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Attendance.WorkAreaChangeRequests;
using ONEVO.Api.Controllers.Tenant.Attendance;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.TimeAttendance.Commands.WorkAreaChangeRequests;
using ONEVO.Application.Features.TimeAttendance.DTOs.Responses;
using ONEVO.Application.Features.TimeAttendance.Queries.WorkAreaChangeRequests;

namespace ONEVO.Tests.Unit.Controllers.Tenant.Attendance;

public sealed class WorkAreaChangeRequestsControllerTests
{
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 8, 25);

    [Fact]
    public async Task Preview_SendsSelfServiceCommandAndReturnsOk()
    {
        var mediator = new Mock<IMediator>();
        var expected = PreviewResponse();
        mediator.Setup(x => x.Send(It.IsAny<PreviewWorkAreaChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkAreaChangeRequestPreviewResponse>.Success(expected));
        var controller = new WorkAreaChangeRequestsController(mediator.Object);

        var result = await controller.Preview(
            new WorkAreaChangeRequestRequest(Date, " REMOTE ", "Reason"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.Is<PreviewWorkAreaChangeRequestCommand>(command =>
                command.Date == Date && command.RequestedWorkArea == " REMOTE " && command.Reason == "Reason"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_SendsSelfServiceCommandAndReturnsCreated()
    {
        var mediator = new Mock<IMediator>();
        var expected = Response();
        mediator.Setup(x => x.Send(It.IsAny<CreateWorkAreaChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkAreaChangeRequestResponse>.Success(expected));
        var controller = new WorkAreaChangeRequestsController(mediator.Object);

        var result = await controller.Create(
            new WorkAreaChangeRequestRequest(Date, "remote", "Reason"), CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Same(expected, created.Value);
    }

    [Fact]
    public async Task Approvals_MapsQueryAndReturnsPagedResult()
    {
        var mediator = new Mock<IMediator>();
        var expected = new PagedResult<WorkAreaChangeRequestResponse>(Array.Empty<WorkAreaChangeRequestResponse>(), 1, 20, 0);
        mediator.Setup(x => x.Send(It.IsAny<ListWorkAreaChangeRequestApprovalsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<WorkAreaChangeRequestResponse>>.Success(expected));
        var controller = new WorkAreaChangeRequestsController(mediator.Object);

        var result = await controller.Approvals(
            Date.AddDays(-1), Date, new PagedRequest { PageNumber = 2, PageSize = 10 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(expected, ok.Value);
        mediator.Verify(x => x.Send(
            It.Is<ListWorkAreaChangeRequestApprovalsQuery>(query =>
                query.From == Date.AddDays(-1) && query.To == Date && query.Paging.PageNumber == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_MapsRouteIdAndReviewComment()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<RejectWorkAreaChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkAreaChangeRequestResponse>.Success(Response()));
        var controller = new WorkAreaChangeRequestsController(mediator.Object);

        await controller.Reject(RequestId, new ReviewWorkAreaChangeRequestRequest("Please explain."), CancellationToken.None);

        mediator.Verify(x => x.Send(
            It.Is<RejectWorkAreaChangeRequestCommand>(command =>
                command.Id == RequestId && command.ReviewComment == "Please explain."),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RequestDtos_DoNotExposeServerOwnedIdentifiers()
    {
        var forbidden = new[]
        {
            "TenantId", "EmployeeId", "LegalEntityId", "ApproverId", "ReviewedById",
            "Status", "ShiftAssignmentId", "AttachmentId"
        };
        var properties = typeof(WorkAreaChangeRequestRequest).GetProperties()
            .Concat(typeof(ReviewWorkAreaChangeRequestRequest).GetProperties())
            .Select(property => property.Name);

        Assert.DoesNotContain(properties, property => forbidden.Contains(property, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateFailure_UsesExistingProblemMapping()
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(x => x.Send(It.IsAny<CreateWorkAreaChangeRequestCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<WorkAreaChangeRequestResponse>.Conflict("Duplicate request."));
        var controller = new WorkAreaChangeRequestsController(mediator.Object);

        var result = await controller.Create(
            new WorkAreaChangeRequestRequest(Date, "remote", "Reason"), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    private static WorkAreaChangeRequestPreviewResponse PreviewResponse()
        => new(Date, "UTC", "onsite", "remote", "Reason", null);

    private static WorkAreaChangeRequestResponse Response()
        => new(RequestId, Guid.NewGuid(), Guid.NewGuid(), "Employee", "UTC", Date,
            "onsite", "remote", "Reason", "pending", Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            null, null, null, null, null);
}
