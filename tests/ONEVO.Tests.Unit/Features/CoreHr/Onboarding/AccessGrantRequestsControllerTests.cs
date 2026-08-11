using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.CoreHr.AccessGrantRequests;
using ONEVO.Api.Controllers.Tenant.CoreHr;
using ONEVO.Api.Filters;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.ApproveAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.Commands.RejectAccessGrantRequest;
using ONEVO.Application.Features.CoreHr.Onboarding.DTOs.Responses;
using ONEVO.Application.Features.CoreHr.Onboarding.Queries.ListOnboardingAccessGrantRequests;

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

    [Fact]
    public async Task List_WithDefaultParams_SendsQueryWithDefaults()
    {
        var response = new OnboardingAccessGrantRequestListPageResponse([], 0, 1, 25);
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingAccessGrantRequestsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingAccessGrantRequestListPageResponse>.Success(response));

        var result = await _sut.List(ct: CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<ListOnboardingAccessGrantRequestsQuery>(q =>
                q.Status == "pending" && q.ActionType == "onboarding" && q.Page == 1 && q.PageSize == 25
                && q.Search == null && q.LegalEntityId == null && q.RequestedRoleId == null),
            It.IsAny<CancellationToken>()), Times.Once);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task List_BindsAllQueryParametersThrough()
    {
        var legalEntityId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var response = new OnboardingAccessGrantRequestListPageResponse([], 0, 2, 10);
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingAccessGrantRequestsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingAccessGrantRequestListPageResponse>.Success(response));

        await _sut.List(
            status: "approved", actionType: "onboarding", page: 2, pageSize: 10, search: "jane",
            legalEntityId: legalEntityId, requestedRoleId: roleId, ct: CancellationToken.None);

        _mediator.Verify(m => m.Send(
            It.Is<ListOnboardingAccessGrantRequestsQuery>(q =>
                q.Status == "approved" && q.ActionType == "onboarding" && q.Page == 2 && q.PageSize == 10
                && q.Search == "jane" && q.LegalEntityId == legalEntityId && q.RequestedRoleId == roleId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_ReturnsProblemOnValidationFailure()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<ListOnboardingAccessGrantRequestsQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<OnboardingAccessGrantRequestListPageResponse>.Failure("status must be one of: pending, approved, rejected, cancelled.", 400));

        var result = await _sut.List(status: "bogus", ct: CancellationToken.None);

        var problem = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    // Security: no tenantId can be supplied by the caller through the route/query for the list
    // endpoint - tenant scoping is resolved server-side only.
    [Fact]
    public void List_HasNoTenantIdParameter()
    {
        var method = typeof(AccessGrantRequestsController).GetMethod(nameof(AccessGrantRequestsController.List))!;
        Assert.DoesNotContain(method.GetParameters(), p => p.Name!.Contains("tenant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void List_RequiresEmployeesWritePermission()
    {
        var method = typeof(AccessGrantRequestsController).GetMethod(nameof(AccessGrantRequestsController.List))!;
        var attribute = method.GetCustomAttribute<RequirePermissionAttribute>();
        Assert.NotNull(attribute);

        var field = typeof(RequirePermissionAttribute).GetField("_permission", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal("employees:write", (string)field!.GetValue(attribute)!);
    }
}
