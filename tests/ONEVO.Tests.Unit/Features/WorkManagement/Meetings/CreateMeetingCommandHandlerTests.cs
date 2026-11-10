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
using ONEVO.Application.Features.WorkManagement.Meetings.RepositoryInterfaces;
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
    private readonly Mock<IMeetingDelegateRepository> _delegates = new();
    private readonly Mock<IMeetingDelegateAudit> _audit = new();

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
        new MeetingApprovalSnapshotBuilder(_employees.Object, _hierarchy.Object, Mock.Of<IMeetingRepository>(), Mock.Of<IMeetingLinkRepository>()),
        _unitOfWork.Object, _clock.Object, _delegates.Object, _employees.Object, _audit.Object);

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
                It.IsAny<CreateMeetingRequest>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
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

    /// <summary>The factory sends the invites last. Their in-app notices are tracked outbox rows, so a direct create must save
    /// after the factory returns, or the transaction commits without them (only the raw-SQL invite emails survive).</summary>
    private List<string> TrackCreateThenSave()
    {
        var steps = new List<string>();
        _factory.Setup(f => f.CreateDirectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MeetingPlacement>(),
                It.IsAny<CreateMeetingRequest>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback(() => steps.Add("create"))
            .ReturnsAsync(Result<MeetingCreatedResponse>.Success(new MeetingCreatedResponse("created", Guid.NewGuid(), Guid.NewGuid(), null, [])));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => steps.Add("save")).ReturnsAsync(1);
        return steps;
    }

    [Fact]
    public async Task ADirectCreate_SavesAfterTheFactorySentTheInvites()
    {
        _placements.Setup(p => p.ResolveAsync(Tenant, Caller, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: true)));
        var steps = TrackCreateThenSave();

        var result = await Handler().Handle(new CreateMeetingCommand(Form()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["create", "save"], steps);
    }

    [Fact]
    public async Task ACreateTheApprovalEngineMakesDirect_SavesAfterTheFactorySentTheInvites()
    {
        _placements.Setup(p => p.ResolveAsync(Tenant, Caller, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: false)));
        _approvals.Setup(a => a.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Direct));
        var steps = TrackCreateThenSave();

        var result = await Handler().Handle(new CreateMeetingCommand(Form()), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["create", "save"], steps);
    }

    // ---- Creating for a principal (spec "Creating a meeting for the principal") ----

    private static readonly Employee Principal = new() { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), FirstName = "Chief", LastName = "Executive" };

    private sealed record FactoryCall(Guid Organizer, Guid OrganizerUser, Guid? CreatedBy);

    private List<FactoryCall> CaptureFactory()
    {
        var calls = new List<FactoryCall>();
        _factory.Setup(f => f.CreateDirectAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<MeetingPlacement>(),
                It.IsAny<CreateMeetingRequest>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback((Guid _, Guid organizer, Guid organizerUser, MeetingPlacement _, CreateMeetingRequest _, string _, Guid? _,
                CancellationToken _, Guid? createdBy) => calls.Add(new FactoryCall(organizer, organizerUser, createdBy)))
            .ReturnsAsync(Result<MeetingCreatedResponse>.Success(new MeetingCreatedResponse("created", Guid.NewGuid(), Guid.NewGuid(), null, [])));
        return calls;
    }

    private void PrincipalDelegatedToCaller()
    {
        _delegates.Setup(d => d.IsActiveDelegateAsync(Tenant, Principal.Id, Caller, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _employees.Setup(e => e.GetByIdAsync(Tenant, Principal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Principal);
    }

    private void DirectFor(Guid organizer)
        => _placements.Setup(p => p.ResolveAsync(Tenant, organizer, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: true)));

    [Fact]
    public async Task ACreateForAPrincipal_UsesThePrincipalAsOrganizer_AndTheCallerAsCreator()
    {
        PrincipalDelegatedToCaller();
        DirectFor(Principal.Id);
        var calls = CaptureFactory();

        var result = await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Principal.Id }), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new FactoryCall(Principal.Id, Principal.UserId, _user.Object.UserId), Assert.Single(calls));
    }

    [Fact]
    public async Task ACreateForSomeoneWhoDidNotDelegate_Is403MeetingDelegateRequired()
    {
        var result = await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Principal.Id }), CancellationToken.None);

        Assert.Equal((403, MeetingErrorCodes.DelegateRequired), (result.StatusCode, result.ErrorCode));
        _placements.VerifyNoOtherCalls();
        _factory.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ACreateForAPrincipal_ResolvesPlacementForThePrincipal()
    {
        PrincipalDelegatedToCaller();
        DirectFor(Principal.Id);
        CaptureFactory();

        await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Principal.Id }), CancellationToken.None);

        _placements.Verify(p => p.ResolveAsync(Tenant, Principal.Id, Project, Module.Id, It.IsAny<CancellationToken>()), Times.Once);
        _placements.Verify(p => p.ResolveAsync(Tenant, Caller, It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnApprovalRequestForAPrincipal_IsSubmittedInThePrincipalsName()
    {
        PrincipalDelegatedToCaller();
        _placements.Setup(p => p.ResolveAsync(Tenant, Principal.Id, Project, Module.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<MeetingPlacement>.Success(new MeetingPlacement(Project, Module.Id, false, Direct: false)));
        WorkAction? submitted = null;
        _approvals.Setup(a => a.SubmitAsync(It.IsAny<WorkAction>(), It.IsAny<CancellationToken>()))
            .Callback((WorkAction action, CancellationToken _) => submitted = action)
            .ReturnsAsync(Result<ApprovalDecision>.Success(ApprovalDecision.Pending(Guid.NewGuid(), Guid.NewGuid())));

        var result = await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Principal.Id }), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Principal.Id, submitted!.ActorEmployeeId);
        _audit.Verify(a => a.RecordForPrincipalAsync(Tenant, null, false, Principal.Id, MeetingAuditActions.Created, It.IsAny<object>(),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ACreateForAPrincipal_RecordsOneAudit_AndACreateForYourselfRecordsNone()
    {
        PrincipalDelegatedToCaller();
        DirectFor(Principal.Id);
        DirectFor(Caller);
        CaptureFactory();

        await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Principal.Id }), CancellationToken.None);
        _audit.Verify(a => a.RecordForPrincipalAsync(Tenant, It.IsAny<Guid?>(), false, Principal.Id, MeetingAuditActions.Created,
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);

        _audit.Invocations.Clear();
        await Handler().Handle(new CreateMeetingCommand(Form()), CancellationToken.None);
        _audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NamingYourselfAsOrganizer_BehavesLikeToday()
    {
        DirectFor(Caller);
        var calls = CaptureFactory();

        var result = await Handler().Handle(new CreateMeetingCommand(Form() with { OrganizerEmployeeId = Caller }), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new FactoryCall(Caller, _user.Object.UserId, _user.Object.UserId), Assert.Single(calls));
        _delegates.VerifyNoOtherCalls();
    }
}
