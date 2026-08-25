using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ONEVO.Api.Contracts.Leave.Policies;
using ONEVO.Api.Controllers.Tenant.Leave;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Leave.Policy.Commands.CloneLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Commands.CreateLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.DTOs.Responses;
using ONEVO.Application.Features.Leave.Policy.Queries.GetLeavePolicy;
using ONEVO.Application.Features.Leave.Policy.Queries.ListLeavePolicies;
using ONEVO.Domain.Features.Leave.Common;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Leave.Policy;

public class LeavePoliciesControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly LeavePoliciesController _sut;
    private readonly Guid _policyId = Guid.NewGuid();
    private readonly Guid _leaveTypeId = Guid.NewGuid();
    private readonly Guid _legalEntityId = Guid.NewGuid();

    public LeavePoliciesControllerTests()
    {
        _sut = new LeavePoliciesController(_mediatorMock.Object);
    }

    [Fact]
    public async Task List_SendsQueryAndReturnsOk()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<ListLeavePoliciesQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<LeavePolicyListItemResponse>>.Success([]));

        var result = await _sut.List(includeInactive: true, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<ListLeavePoliciesQuery>(q => q.IncludeInactive),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Get_SendsQueryAndReturnsOk()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<GetLeavePolicyQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var result = await _sut.Get(_policyId, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<GetLeavePolicyQuery>(q => q.LeavePolicyId == _policyId),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_MapsRequestToCommand()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<CreateLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var request = SampleCreateRequest(confirm: true);

        var result = await _sut.Create(request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CreateLeavePolicyCommand>(c =>
                c.Name == "LK Policy" &&
                c.Country == "LK" &&
                c.ConfirmReplaceExistingLegalEntityAssignments),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Clone_MapsRequestToCommand()
    {
        var response = SampleResponse();
        _mediatorMock.Setup(m => m.Send(It.IsAny<CloneLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Success(response));

        var request = new CloneLeavePolicyRequest("LK Copy", "LK", [_legalEntityId], new DateOnly(2026, 1, 1), false);

        var result = await _sut.Clone(_policyId, request, CancellationToken.None);

        _mediatorMock.Verify(m => m.Send(
            It.Is<CloneLeavePolicyCommand>(c => c.SourcePolicyId == _policyId && c.Name == "LK Copy"),
            It.IsAny<CancellationToken>()), Times.Once);
        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Create_Conflict_ReturnsProblem409()
    {
        _mediatorMock.Setup(m => m.Send(It.IsAny<CreateLeavePolicyCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<LeavePolicyResponse>.Conflict("Legal Entity Acme already has an active policy. Activating this policy will replace it. Continue?"));

        var result = await _sut.Create(SampleCreateRequest(confirm: false), CancellationToken.None);

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(409);
    }

    private CreateLeavePolicyRequest SampleCreateRequest(bool confirm) => new(
        "LK Policy",
        "Sri Lanka annual leave policy",
        "LK",
        null,
        LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately,
        null,
        LeaveProrationMethods.CalendarDays,
        false,
        0,
        null,
        7,
        14,
        0.5m,
        20m,
        LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1),
        [new CreateLeavePolicyTypeRuleRequest(_leaveTypeId, 20m, null, 5m, 3)],
        [],
        [_legalEntityId],
        confirm);

    private LeavePolicyResponse SampleResponse() => new(
        _policyId, "LK Policy", null, "LK", null, LeaveAccrualMethods.Annual,
        LeaveAccrualStarts.Immediately, null, LeaveProrationMethods.CalendarDays, false,
        0, null, 7, 14, 0.5m, 20m, LeaveApprovalModes.AnyOne,
        new DateOnly(2026, 1, 1), 1, true, [], [], [], DateTimeOffset.UtcNow, null);
}
