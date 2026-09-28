using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Unit.Features.Auth;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public sealed class EfWorkApprovalHistoryRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid EmployeeId = Guid.NewGuid();
    private static readonly Guid OtherEmployeeId = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public EfWorkApprovalHistoryRepositoryTests()
    {
        _connectionString = $"Data Source=approval_history_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    [Fact]
    public async Task ListForEmployee_ReturnsOnlyProjectRequestsWhereEmployeeParticipated()
    {
        await using var db = CreateContext();
        var objectiveId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.Projects.Add(new Project
        {
            Id = ProjectId, TenantId = TenantId, Name = "Portal", Identifier = "PORTAL",
            LeadId = EmployeeId, StartDate = DateOnly.FromDateTime(now.Date),
            TargetDate = DateOnly.FromDateTime(now.Date.AddDays(30))
        });
        db.Objectives.Add(new Objective
        {
            Id = objectiveId, TenantId = TenantId, ProjectId = ProjectId, Title = "Payments",
            OwnerId = OtherEmployeeId, ReportingManagerId = OtherEmployeeId,
            StartDate = DateOnly.FromDateTime(now.Date), EndDate = DateOnly.FromDateTime(now.Date.AddDays(30))
        });
        db.WorkTasks.Add(new WorkTask
        {
            Id = taskId, TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId,
            Title = "Audit events", ShortId = "PORTAL-1"
        });
        // Decided by the employee.
        db.WorkApprovalRequests.Add(TaskRequest(WorkActionTypes.TaskCreate, null, objectiveId,
            requestedBy: OtherEmployeeId, approver: EmployeeId, status: WorkApprovalRequestStatuses.Approved,
            decidedBy: EmployeeId, payload: "{\"title\":\"Audit events\"}", decidedAt: now));
        // Requested by the employee.
        db.WorkApprovalRequests.Add(TaskRequest(WorkActionTypes.TaskEdit, taskId, objectiveId,
            requestedBy: EmployeeId, approver: OtherEmployeeId, status: WorkApprovalRequestStatuses.Pending,
            decidedBy: null, payload: "{\"title\":\"Audit trail\"}"));
        // Waiting on the employee as the stored approver.
        db.WorkApprovalRequests.Add(TaskRequest(WorkActionTypes.TaskDelete, taskId, objectiveId,
            requestedBy: OtherEmployeeId, approver: EmployeeId, status: WorkApprovalRequestStatuses.Pending,
            decidedBy: null, payload: "{}"));
        // The employee was the approver once, but someone else decided: not participated.
        db.WorkApprovalRequests.Add(TaskRequest(WorkActionTypes.TaskDelete, taskId, objectiveId,
            requestedBy: OtherEmployeeId, approver: EmployeeId, status: WorkApprovalRequestStatuses.Rejected,
            decidedBy: OtherEmployeeId, payload: "{\"note\":\"Unrelated\"}"));
        db.ObjectiveChangeRequests.Add(new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ObjectiveId = objectiveId,
            RequestedById = EmployeeId, ReportingManagerId = OtherEmployeeId,
            RequestType = ObjectiveChangeRequestTypes.ExtendAllocation,
            Status = ObjectiveChangeRequestStatuses.Pending,
            PayloadJson = "{\"requestedAdditionalHours\":8,\"reason\":\"Finish integration\"}"
        });
        db.ProjectMemberInvitations.Add(new ProjectMemberInvitation
        {
            Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ObjectiveId = objectiveId,
            InvitedById = EmployeeId, InvitedEmployeeId = OtherEmployeeId,
            InviteType = ProjectInvitationTypes.Member, Status = ProjectInvitationStatuses.Accepted,
            DecidedAt = now
        });
        db.WorkApprovalRequests.Add(TaskRequest(WorkActionTypes.TaskCreate, null, objectiveId,
            requestedBy: Guid.NewGuid(), approver: Guid.NewGuid(), status: WorkApprovalRequestStatuses.Rejected,
            decidedBy: Guid.NewGuid(), payload: "{\"title\":\"Unrelated\"}"));
        // Status-template request the employee made (project target, no task).
        var template = TaskRequest(WorkActionTypes.ProjectStatusTemplateChange, null, objectiveId,
            requestedBy: EmployeeId, approver: OtherEmployeeId, status: WorkApprovalRequestStatuses.Pending,
            decidedBy: null, payload: "{\"Changes\":{},\"Note\":null}");
        template.TargetType = WorkTargetTypes.Project;
        template.TargetTitle = "Portal";
        db.WorkApprovalRequests.Add(template);
        await db.SaveChangesAsync();

        var repository = new EfWorkApprovalHistoryRepository(db);
        var items = await repository.ListForEmployeeAsync(TenantId, ProjectId, EmployeeId);

        Assert.Equal(6, items.Count);
        Assert.Contains(items, item => item.Kind == "task_status_change" && item.SubjectTitle == "Task statuses" && item.RequestedById == EmployeeId);
        Assert.Contains(items, item => item.Kind == "task_creation" && item.DecidedById == EmployeeId && item.SubjectTitle == "Audit events");
        Assert.Contains(items, item => item.Kind == "task_edit" && item.RequestedById == EmployeeId);
        Assert.Contains(items, item => item.Kind == "task_delete" && item.Status == "pending" && item.ApproverId == EmployeeId);
        Assert.Contains(items, item => item.Kind == "allocation_extend" && item.Status == "pending");
        Assert.Contains(items, item => item.Kind == "objective_invitation" && item.PayloadJson!.Contains("member"));
        Assert.DoesNotContain(items, item => item.PayloadJson?.Contains("Unrelated") == true);
    }

    private static WorkApprovalRequest TaskRequest(
        string actionType, Guid? targetId, Guid positionId, Guid requestedBy, Guid approver, string status,
        Guid? decidedBy, string payload, DateTimeOffset? decidedAt = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId, ActionType = actionType,
        TargetType = WorkTargetTypes.Task, TargetId = targetId, TargetTitle = "Audit events",
        PositionObjectiveId = positionId, ApproverSource = WorkApprovalSources.Hierarchy,
        ApproverEmployeeId = approver, RequestedByEmployeeId = requestedBy, PayloadJson = payload,
        Status = status, DecidedByEmployeeId = decidedBy, DecidedAt = decidedAt
    };

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SqliteTestApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), _clock),
            new SoftDeleteInterceptor(_clock),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private sealed class NoOpPublisher : IPublisher
    {
        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;
    }
}
