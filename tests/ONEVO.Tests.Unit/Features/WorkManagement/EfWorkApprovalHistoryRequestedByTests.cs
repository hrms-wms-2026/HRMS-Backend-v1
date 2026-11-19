using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;

namespace ONEVO.Tests.Unit.Features.WorkManagement;

public sealed class EfWorkApprovalHistoryRequestedByTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _otherEmployeeId = Guid.NewGuid();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Mock<IDateTimeProvider> _clock = new();
    private static readonly DateTimeOffset InWindow = DateTimeOffset.Parse("2026-09-10T00:00:00+00:00");
    private static readonly DateTimeOffset BeforeWindow = DateTimeOffset.Parse("2026-07-01T00:00:00+00:00");
    private static readonly DateTimeOffset From = DateTimeOffset.Parse("2026-09-01T00:00:00+00:00");
    private static readonly DateTimeOffset ToExclusive = DateTimeOffset.Parse("2026-10-01T00:00:00+00:00");

    private Objective Obj() => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Title = "Checkout",
        OwnerId = Guid.NewGuid(), IsActive = true
    };

    private WorkApprovalRequest Request(string actionType, Guid requestedBy, Guid? approver = null, Guid? projectId = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = projectId ?? _projectId, ActionType = actionType,
        TargetType = actionType.StartsWith("module.") ? WorkTargetTypes.Module
            : actionType.StartsWith("sprint.") ? WorkTargetTypes.Sprint
            : actionType.StartsWith("project.") ? WorkTargetTypes.Project : WorkTargetTypes.Task,
        TargetId = Guid.NewGuid(), TargetTitle = "Target", ApproverEmployeeId = approver ?? Guid.NewGuid(),
        RequestedByEmployeeId = requestedBy
    };

    [Fact]
    public async Task ListRequestedBy_ReturnsEachKindTheEmployeeRequested_AcrossProjects_AndNothingElse()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        db.Objectives.Add(objective);

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        db.WorkApprovalRequests.AddRange(
            Request(WorkActionTypes.TaskCreate, _employeeId),
            Request(WorkActionTypes.TaskCreate, _otherEmployeeId),
            Request(WorkActionTypes.TaskEdit, _employeeId, projectId: Guid.NewGuid()),
            Request(WorkActionTypes.TaskDelete, _employeeId),
            Request(WorkActionTypes.ModuleEdit, _employeeId),
            Request(WorkActionTypes.SprintStart, _employeeId),
            Request(WorkActionTypes.ProjectStatusTemplateChange, _employeeId));
        db.ProjectMemberInvitations.AddRange(
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _employeeId, InvitedById = _otherEmployeeId },
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _otherEmployeeId, InvitedById = _employeeId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(
            new[] { "objective_edit", "objective_invitation", "sprint_change", "task_creation", "task_delete", "task_edit", "task_status_change" },
            records.Select(r => r.Kind).OrderBy(k => k).ToArray());
        Assert.All(records, r => Assert.Equal("pending", r.Status));
        Assert.Equal("Task statuses", Assert.Single(records, r => r.Kind == "task_status_change").SubjectTitle);
    }

    [Fact]
    public async Task ListRequestedBy_ExcludesRequestsCreatedOutsideTheWindow()
    {
        await using var db = BuildInMemoryDb();

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        var inside = Request(WorkActionTypes.TaskCreate, _employeeId);
        db.WorkApprovalRequests.Add(inside);
        await db.SaveChangesAsync();

        _clock.SetupGet(c => c.UtcNow).Returns(BeforeWindow);
        db.WorkApprovalRequests.Add(Request(WorkActionTypes.TaskCreate, _employeeId));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(inside.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListRequestedBy_MapsModuleActionsToTheirKinds_AndFillsApproverAndDecision()
    {
        await using var db = BuildInMemoryDb();
        var manager = Guid.NewGuid();
        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        var extend = Request(WorkActionTypes.ModuleAllocationExtend, _employeeId, manager);
        extend.Status = WorkApprovalRequestStatuses.Approved;
        extend.DecidedByEmployeeId = manager;
        db.WorkApprovalRequests.AddRange(extend, Request(WorkActionTypes.ModuleTransfer, _employeeId, manager));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        var extendRecord = Assert.Single(records, r => r.Kind == "allocation_extend");
        Assert.Equal("approved", extendRecord.Status);
        Assert.Equal(manager, extendRecord.ApproverId);
        Assert.Equal(manager, extendRecord.DecidedById);
        var transfer = Assert.Single(records, r => r.Kind == "objective_change");
        Assert.Equal(manager, transfer.ApproverId);
        Assert.Null(transfer.DecidedById);
    }

    private ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new Mock<ICurrentUser>().Object, _clock.Object),
            new SoftDeleteInterceptor(_clock.Object),
            new DomainEventDispatchInterceptor(new Mock<MediatR.IPublisher>().Object),
            new Mock<ITenantContext>().Object);
    }
}
