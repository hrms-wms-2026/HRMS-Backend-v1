using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.ObjectiveChangeRequests.Entities;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectInvitations.Entities;
using ONEVO.Domain.Features.WorkManagement.Tasks.Entities;
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

    private Objective Obj(bool isDefault = false) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, Title = isDefault ? "Root" : "Checkout",
        OwnerId = Guid.NewGuid(), IsDefault = isDefault, IsActive = true
    };

    [Fact]
    public async Task ListRequestedBy_ReturnsEachKindTheEmployeeRequested_AndNothingElse()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        var root = Obj(isDefault: true);
        var task = new WorkTask
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id,
            StatusId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), ShortId = "WEB-1", Title = "Fix cart"
        };
        db.Objectives.AddRange(objective, root);
        db.WorkTasks.Add(task);

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        db.TaskCreationRequests.AddRange(
            new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId },
            new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _otherEmployeeId });
        db.TaskEditRequests.Add(new TaskEditRequest { Id = Guid.NewGuid(), TenantId = _tenantId, TaskId = task.Id, RequestedByEmployeeId = _employeeId });
        db.ObjectiveChangeRequests.Add(new ObjectiveChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.Edit,
            RequestedById = _employeeId, ReportingManagerId = Guid.NewGuid()
        });
        db.TaskStatusChangeRequests.Add(new TaskStatusChangeRequest
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, RequestedByEmployeeId = _employeeId
        });
        db.ProjectMemberInvitations.AddRange(
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _employeeId, InvitedById = _otherEmployeeId },
            new ProjectMemberInvitation { Id = Guid.NewGuid(), TenantId = _tenantId, ProjectId = _projectId, ObjectiveId = objective.Id, InvitedEmployeeId = _otherEmployeeId, InvitedById = _employeeId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(
            new[] { "objective_edit", "objective_invitation", "task_creation", "task_edit", "task_status_change" },
            records.Select(r => r.Kind).OrderBy(k => k).ToArray());
        Assert.All(records, r => Assert.Equal("pending", r.Status));
    }

    [Fact]
    public async Task ListRequestedBy_ExcludesRequestsCreatedOutsideTheWindow()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        db.Objectives.Add(objective);

        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        var inside = new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId };
        db.TaskCreationRequests.Add(inside);
        await db.SaveChangesAsync();

        _clock.SetupGet(c => c.UtcNow).Returns(BeforeWindow);
        db.TaskCreationRequests.Add(new TaskCreationRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestedByEmployeeId = _employeeId });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        Assert.Equal(inside.Id, Assert.Single(records).Id);
    }

    [Fact]
    public async Task ListRequestedBy_MapsObjectiveChangeTypesToTheirKinds_AndFillsApproverAndDecision()
    {
        await using var db = BuildInMemoryDb();
        var objective = Obj();
        var manager = Guid.NewGuid();
        db.Objectives.Add(objective);
        _clock.SetupGet(c => c.UtcNow).Returns(InWindow);
        db.ObjectiveChangeRequests.AddRange(
            new ObjectiveChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.ExtendAllocation, RequestedById = _employeeId, ReportingManagerId = manager, Status = ObjectiveChangeRequestStatuses.Approved },
            new ObjectiveChangeRequest { Id = Guid.NewGuid(), TenantId = _tenantId, ObjectiveId = objective.Id, RequestType = ObjectiveChangeRequestTypes.Transfer, RequestedById = _employeeId, ReportingManagerId = manager });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var records = await new EfWorkApprovalHistoryRepository(db)
            .ListRequestedByEmployeeAsync(_tenantId, _employeeId, From, ToExclusive);

        var extend = Assert.Single(records, r => r.Kind == "allocation_extend");
        Assert.Equal("approved", extend.Status);
        Assert.Equal(manager, extend.ApproverId);
        Assert.Equal(manager, extend.DecidedById);
        var transfer = Assert.Single(records, r => r.Kind == "objective_change");
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
