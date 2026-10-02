using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Approvals.Comments;
using ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.CreateApprovalComment;
using ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.EditApprovalComment;
using ONEVO.Application.Features.WorkManagement.Approvals.Comments.Commands.ReplyApprovalComment;
using ONEVO.Application.Features.WorkManagement.Approvals.Comments.Queries.ListApprovalComments;
using ONEVO.Application.Features.WorkManagement.Approvals.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Hierarchy;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.ProjectInvitations.RepositoryInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Notifications.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Tests.Unit.Fakes;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public class ApprovalCommentHandlersTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();
    private readonly Guid _root = Guid.NewGuid();
    private readonly Guid _p = Guid.NewGuid();

    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly Mock<ICallerIdentityResolver> _identity = new();
    private readonly Mock<IWorkApprovalRequestRepository> _requests = new();
    private readonly Mock<IWorkHierarchyService> _hierarchy = new();
    private readonly Mock<IProjectMemberInvitationRepository> _invitations = new();
    private readonly Mock<IWorkNotificationEngine> _notifications = new();
    private readonly InMemoryApprovalCommentRepository _comments = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly WorkApprovalRequest _request;
    private readonly ProjectMemberInvitation _invitation;

    public ApprovalCommentHandlersTests()
    {
        _currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        _currentUser.SetupGet(x => x.TenantId).Returns(TenantId);
        _currentUser.SetupGet(x => x.UserId).Returns(UserId);
        _identity.Setup(x => x.ResolveDisplayNamesByEmployeeIdAsync(TenantId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [Requester] = "Bala", [Approver] = "Anu", [Stranger] = "Chen" });
        _hierarchy.Setup(x => x.LoadTreeAsync(TenantId, ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProjectModuleTree(new[]
            {
                new Objective { Id = _root, OwnerId = Guid.NewGuid(), IsDefault = true },
                new Objective { Id = _p, ParentObjectiveId = _root, OwnerId = Approver, Title = "Payments" },
            }));
        _request = new WorkApprovalRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, PositionObjectiveId = _p,
            ApproverSource = WorkApprovalSources.Hierarchy, ApproverEmployeeId = Approver, RequestedByEmployeeId = Requester,
            Status = WorkApprovalRequestStatuses.Pending, ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task,
            TargetId = Guid.NewGuid(), TargetTitle = "Audit events"
        };
        _requests.Setup(x => x.GetByIdForTenantAsync(TenantId, _request.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_request);
        _invitation = new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = _p,
            InvitedEmployeeId = Stranger, InvitedById = Approver, Status = ProjectInvitationStatuses.Pending
        };
        _invitations.Setup(x => x.GetByIdForTenantAsync(TenantId, _invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_invitation);
    }

    private void Caller(Guid employeeId)
        => _identity.Setup(x => x.ResolveCallerEmployeeIdAsync(TenantId, UserId, It.IsAny<CancellationToken>())).ReturnsAsync(employeeId);

    private ApprovalCommentAccess Access() => new(_requests.Object, _hierarchy.Object, _invitations.Object);

    private CreateApprovalCommentCommandHandler Create() => new(_currentUser.Object, _identity.Object, Access(), _comments, _notifications.Object, _uow);
    private ReplyApprovalCommentCommandHandler Reply() => new(_currentUser.Object, _identity.Object, Access(), _comments, _notifications.Object, _uow);
    private EditApprovalCommentCommandHandler Edit() => new(_currentUser.Object, _identity.Object, _comments, _uow);
    private ListApprovalCommentsQueryHandler List() => new(_currentUser.Object, _identity.Object, Access(), _comments);

    private void VerifyNotified(Guid recipient)
        => _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Commented && e.RecipientEmployeeIds.Contains(recipient)), It.IsAny<CancellationToken>()), Times.Once);

    [Fact]
    public async Task Requester_comments_on_own_request_and_the_approver_is_notified()
    {
        Caller(Requester);

        var result = await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "  Can we keep the old date?  "), default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Content.Should().Be("Can we keep the old date?");
        result.Value.AuthorName.Should().Be("Bala");
        result.Value.CanEdit.Should().BeTrue();
        _comments.Items.Should().ContainSingle(c => c.SubjectId == _request.Id && c.EmployeeId == Requester && c.ProjectId == ProjectId);
        _uow.SaveCallCount.Should().Be(1);
        VerifyNotified(Approver);
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.ApprovalRequestId == _request.Id && e.ActionType == WorkActionTypes.TaskEdit), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Stranger_cannot_comment()
    {
        Caller(Stranger);
        (await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "hi"), default)).StatusCode.Should().Be(404);
        _comments.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public async Task Empty_content_is_rejected(string content)
    {
        Caller(Requester);
        var result = await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, content), default);
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Comment cannot be empty.");
    }

    [Fact]
    public async Task Too_long_content_is_rejected()
    {
        Caller(Requester);
        var result = await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, new string('x', 4001)), default);
        result.Error.Should().Be("Comment is too long.");
    }

    [Fact]
    public async Task Reply_to_a_reply_attaches_to_the_top_level_comment_and_notifies()
    {
        Caller(Requester);
        var top = (await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "top"), default)).Value!;
        Caller(Approver);
        var first = (await Reply().Handle(new ReplyApprovalCommentCommand(top.Id, "first"), default)).Value!;

        Caller(Requester);
        var second = await Reply().Handle(new ReplyApprovalCommentCommand(first.Id, "second"), default);

        second.IsSuccess.Should().BeTrue();
        first.ParentCommentId.Should().Be(top.Id);
        second.Value!.ParentCommentId.Should().Be(top.Id);
        // The approver's reply notifies the requester (the engine drops the actor from the recipients).
        _notifications.Verify(x => x.NotifyAsync(It.Is<WorkNotificationEvent>(e =>
            e.Kind == WorkNotificationKinds.Commented && e.ActorEmployeeId == Approver
            && e.RecipientEmployeeIds.Contains(Requester)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Author_edits_own_comment_others_are_forbidden()
    {
        Caller(Requester);
        var comment = (await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "draft"), default)).Value!;

        var edited = await Edit().Handle(new EditApprovalCommentCommand(comment.Id, "final"), default);
        edited.IsSuccess.Should().BeTrue();
        edited.Value!.IsEdited.Should().BeTrue();
        _comments.Items.Single().Content.Should().Be("final");

        Caller(Approver);
        (await Edit().Handle(new EditApprovalCommentCommand(comment.Id, "hijack"), default)).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task List_is_oldest_first_with_names_and_can_edit_only_on_own()
    {
        Caller(Requester);
        await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "one"), default);
        Caller(Approver);
        await Create().Handle(new CreateApprovalCommentCommand("approval", _request.Id, "two"), default);

        var list = (await List().Handle(new ListApprovalCommentsQuery("approval", _request.Id), default)).Value!;

        list.Select(c => c.Content).Should().Equal("one", "two");
        list.Select(c => c.AuthorName).Should().Equal("Bala", "Anu");
        list.Select(c => c.CanEdit).Should().Equal(false, true);

        Caller(Stranger);
        (await List().Handle(new ListApprovalCommentsQuery("approval", _request.Id), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Invitation_thread_is_open_to_invitee_and_inviter_only()
    {
        Caller(Stranger); // the invitee
        var result = await Create().Handle(new CreateApprovalCommentCommand("invitation", _invitation.Id, "What is the scope?"), default);
        result.IsSuccess.Should().BeTrue();
        VerifyNotified(Approver);

        Caller(Approver); // the inviter
        (await List().Handle(new ListApprovalCommentsQuery("invitation", _invitation.Id), default)).Value.Should().HaveCount(1);

        Caller(Requester);
        (await Create().Handle(new CreateApprovalCommentCommand("invitation", _invitation.Id, "hi"), default)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unknown_subject_type_is_not_found()
    {
        Caller(Requester);
        (await Create().Handle(new CreateApprovalCommentCommand("task", _request.Id, "hi"), default)).StatusCode.Should().Be(404);
    }

    /// <summary>Stores comments in a list; CreatedAt increases with every add so ordering is deterministic.</summary>
    internal sealed class InMemoryApprovalCommentRepository : IWorkApprovalCommentRepository
    {
        private DateTimeOffset _clock = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
        public List<WorkApprovalComment> Items { get; } = new();

        public Task AddAsync(WorkApprovalComment comment, CancellationToken ct = default)
        {
            _clock = _clock.AddMinutes(1);
            comment.CreatedAt = _clock;
            Items.Add(comment);
            return Task.CompletedTask;
        }

        public Task<WorkApprovalComment?> GetTrackedByIdForTenantAsync(Guid tenantId, Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(c => c.TenantId == tenantId && c.Id == id));

        public Task<IReadOnlyList<WorkApprovalComment>> ListBySubjectAsync(Guid tenantId, string subjectType, Guid subjectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkApprovalComment>>(Items
                .Where(c => c.TenantId == tenantId && c.SubjectType == subjectType && c.SubjectId == subjectId)
                .OrderBy(c => c.CreatedAt).ToList());

        public Task<IReadOnlyDictionary<Guid, int>> CountBySubjectsAsync(Guid tenantId, string subjectType, IReadOnlyCollection<Guid> subjectIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, int>>(Items
                .Where(c => c.TenantId == tenantId && c.SubjectType == subjectType && subjectIds.Contains(c.SubjectId))
                .GroupBy(c => c.SubjectId).ToDictionary(g => g.Key, g => g.Count()));

        public void Update(WorkApprovalComment comment) { }
    }
}
