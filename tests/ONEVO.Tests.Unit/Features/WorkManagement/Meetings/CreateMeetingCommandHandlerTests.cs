using Microsoft.Extensions.Options;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.DecideWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.Commands.RevertWorkApprovalRequest;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Meetings;
using ONEVO.Application.Features.WorkManagement.Meetings.Commands.CreateMeeting;
using ONEVO.Application.Features.WorkManagement.Meetings.Contracts;
using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Infrastructure.Services.WorkManagement.Meetings;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>D4 request path and the flag gate of POST /meetings; meeting approvals are never edited or reverted (spec 5.2).</summary>
public sealed class CreateMeetingCommandHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Caller = Guid.NewGuid();
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Objective Module = new() { Id = Guid.NewGuid(), ProjectId = Project, Title = "Delivery", IsActive = true };

    private readonly Mock<ICurrentUser> _user = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IMeetingPlacementResolver> _placements = new();
    private readonly Mock<IMeetingSeriesFactory> _factory = new();
    private readonly Mock<IWorkApprovalEngine> _approvals = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IEmployeeRepository> _employees = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public CreateMeetingCommandHandlerTests()
    {
        _user.SetupGet(u => u.IsAuthenticated).Returns(true);
        _user.SetupGet(u => u.TenantId).Returns(Tenant);
        _user.SetupGet(u => u.UserId).Returns(Guid.NewGuid());
        _identity.Setup(i => i.ResolveCallerEmployeeIdAsync(Tenant, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        _hierarchy.Setup(h => h.LoadTreeAsync(Tenant, Project, It.IsAny<CancellationToken>())).ReturnsAsync(new ProjectModuleTree([Module]));
        _employees.Setup(e => e.ListByIdsAsync(Tenant, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Employee> { [Caller] = new() { Id = Caller, FirstName = "Priya", LastName = "K" } });
        _unitOfWork.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<Result<MeetingCreatedResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<Result<MeetingCreatedResponse>>> op, CancellationToken ct) => op(ct));
    }

    private CreateMeetingCommandHandler Handler(bool createOn = true) => new(
        Options.Create(new MeetingsOptions { Create = createOn }), _user.Object, _identity.Object,
        new MeetingCreateValidator(new IcalNetMeetingRecurrence()), _placements.Object, _factory.Object, _approvals.Object,
        _hierarchy.Object, _employees.Object, _unitOfWork.Object, _clock.Object);

    private static CreateMeetingRequest Form() => new(Project, Module.Id, "Design review", "Agenda", null, false,
        new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), 45, false, "UTC", "FREQ=WEEKLY", [], ["client@example.com"], null);

    [Fact]
    public async Task AMembersRequest_SubmitsAMeetingCreateApproval_AndCreatesNothing()
    {
        var requestId = Guid.NewGuid();
        _placements.Setup(p => p.ResolveAsync(Tenant, Caller, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: false)));
        WorkAction? submitted = null;
        _approvals.Setup(a => a.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .Callback((WorkAction action, CancellationToken _) => submitted = action)
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(requestId, Guid.NewGuid())));

        var result = await Handler().Handle(new CreateMeetingCommand(Form()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(("requested", requestId), (result.Value!.Outcome, result.Value.ApprovalRequestId));
        Assert.Equal((WorkActionTypes.MeetingCreate, WorkTargetTypes.Meeting, Module.Id), (submitted!.ActionType, submitted.TargetType, submitted.TargetModuleId));
        Assert.Contains("\"form\"", submitted.PayloadJson);
        Assert.Contains("\"snapshot\"", submitted.PayloadJson);
        Assert.Contains("\"guestEmails\":[\"client@example.com\"]", submitted.PayloadJson);
        _factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenMeetingsAreDisabled_NothingRuns()
    {
        var result = await Handler(createOn: false).Handle(new CreateMeetingCommand(Form()), CancellationToken.None);

        Assert.Equal((503, MeetingErrorCodes.MeetingsDisabled), (result.StatusCode, result.ErrorCode));
        _placements.VerifyNoOtherCalls();
        _factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ABusySeries_Is409MeetingBusy()
    {
        _placements.Setup(p => p.ResolveAsync(Tenant, Caller, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: true)));
        _factory.Setup(f => f.CreateDirectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MeetingPlacement>(),
                It.IsAny<CreateMeetingRequest>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MeetingBusyException(Guid.NewGuid(), new TimeoutException()));

        var result = await Handler().Handle(new CreateMeetingCommand(Form()), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.Busy), (result.StatusCode, result.ErrorCode));
    }

    private static Mock<IWorkApprovalRequestRepository> RequestsWith(WorkApprovalRequest request)
    {
        var requests = new Mock<IWorkApprovalRequestRepository>();
        requests.Setup(r => r.GetTrackedByIdForTenantAsync(Tenant, request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(request);
        return requests;
    }

    private static WorkApprovalRequest MeetingRequest(string status) => new()
    {
        Id = Guid.NewGuid(), TenantId = Tenant, ActionType = WorkActionTypes.MeetingCreate, Status = status,
        DecidedByEmployeeId = Caller, DecidedAt = DateTimeOffset.UtcNow, PayloadJson = "{}",
    };

    [Fact]
    public async Task AMeetingApproval_CannotBeApprovedWithAnEditedPayload()
    {
        var request = MeetingRequest(WorkApprovalRequestStatuses.Pending);
        var handler = new DecideWorkApprovalRequestCommandHandler(_user.Object, _identity.Object, RequestsWith(request).Object,
            _hierarchy.Object, new Mock<IApprovalActionApplierRegistry>().Object, new Mock<IWorkNotificationEngine>().Object, _unitOfWork.Object);

        var result = await handler.Handle(new DecideWorkApprovalRequestCommand(request.Id, WorkApprovalDecision.Approve, "{\"form\":{}}", null),
            CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.ApprovalNotEditable), (result.StatusCode, result.ErrorCode));
    }

    [Fact]
    public async Task AMeetingApproval_CannotBeReverted()
    {
        var request = MeetingRequest(WorkApprovalRequestStatuses.Approved);
        var handler = new RevertWorkApprovalRequestCommandHandler(_user.Object, _identity.Object, RequestsWith(request).Object,
            new Mock<IApprovalActionReverterRegistry>().Object, new Mock<IWorkNotificationEngine>().Object, _unitOfWork.Object);

        var result = await handler.Handle(new RevertWorkApprovalRequestCommand(request.Id), CancellationToken.None);

        Assert.Equal((409, MeetingErrorCodes.ApprovalNotEditable), (result.StatusCode, result.ErrorCode));
    }
}
