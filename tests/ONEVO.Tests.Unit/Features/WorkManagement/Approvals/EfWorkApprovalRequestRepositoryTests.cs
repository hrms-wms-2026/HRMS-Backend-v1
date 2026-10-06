using FluentAssertions;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Persistence.Repositories.WorkManagement;
using ONEVO.Tests.Unit.Features.Auth;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Approvals;

public sealed class EfWorkApprovalRequestRepositoryTests : IDisposable
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();

    private readonly string _connectionString;
    private readonly SqliteConnection _masterConnection;
    private readonly TestClock _clock = new();

    public EfWorkApprovalRequestRepositoryTests()
    {
        _connectionString = $"Data Source=approval_requests_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False";
        _masterConnection = new SqliteConnection(_connectionString);
        _masterConnection.Open();

        using var schemaContext = CreateContext();
        schemaContext.Database.EnsureCreated();
    }

    public void Dispose() => _masterConnection.Dispose();

    private static WorkApprovalRequest NewRequest(Guid? targetId, string status = WorkApprovalRequestStatuses.Pending,
        Guid? requestedBy = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantId, ProjectId = ProjectId,
        ActionType = WorkActionTypes.TaskEdit, TargetType = WorkTargetTypes.Task, TargetId = targetId,
        TargetTitle = "Audit events", ApproverEmployeeId = Approver,
        RequestedByEmployeeId = requestedBy ?? Requester, Status = status
    };

    [Fact]
    public async Task HasPending_TrueOnlyForPendingSameTargetAndAction()
    {
        var targetId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(NewRequest(targetId));
            db.WorkApprovalRequests.Add(NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Rejected));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        (await repo.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit)).Should().BeTrue();
        (await repo.HasPendingAsync(TenantId, WorkTargetTypes.Task, targetId, WorkActionTypes.TaskDelete)).Should().BeFalse();
        (await repo.HasPendingAsync(Guid.NewGuid(), WorkTargetTypes.Task, targetId, WorkActionTypes.TaskEdit)).Should().BeFalse();
    }

    [Fact]
    public async Task GetPendingTargetIds_ReturnsOnlyPendingTargetsAmongTheGivenIds()
    {
        var pendingTarget = Guid.NewGuid();
        var decidedTarget = Guid.NewGuid();
        var untouchedTarget = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(NewRequest(pendingTarget));
            db.WorkApprovalRequests.Add(NewRequest(decidedTarget, WorkApprovalRequestStatuses.Approved));
            // A pending request for a target NOT in the queried id list must not leak in.
            db.WorkApprovalRequests.Add(NewRequest(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        var ids = await repo.GetPendingTargetIdsAsync(
            TenantId, WorkTargetTypes.Task, new[] { pendingTarget, decidedTarget, untouchedTarget });

        ids.Should().BeEquivalentTo(new[] { pendingTarget });
    }

    [Fact]
    public async Task GetPendingTargetIds_EmptyIdList_ReturnsEmptyWithoutQuerying()
    {
        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        var ids = await repo.GetPendingTargetIdsAsync(TenantId, WorkTargetTypes.Task, Array.Empty<Guid>());

        ids.Should().BeEmpty();
    }

    [Fact]
    public async Task ListTrackedPendingByAction_ReturnsOnlyPendingOfThatActionInThatProject()
    {
        var match = NewRequest(null);
        match.ActionType = WorkActionTypes.ProjectStatusTemplateChange;
        match.TargetType = WorkTargetTypes.Project;
        var decided = NewRequest(null, WorkApprovalRequestStatuses.Stale);
        decided.ActionType = WorkActionTypes.ProjectStatusTemplateChange;
        var otherAction = NewRequest(Guid.NewGuid());
        var otherProject = NewRequest(null);
        otherProject.ActionType = WorkActionTypes.ProjectStatusTemplateChange;
        otherProject.ProjectId = Guid.NewGuid();
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.AddRange(match, decided, otherAction, otherProject);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        var rows = await repo.ListTrackedPendingByActionAsync(TenantId, ProjectId, WorkActionTypes.ProjectStatusTemplateChange);

        rows.Select(r => r.Id).Should().Equal(match.Id);
        read.Entry(rows[0]).State.Should().Be(EntityState.Unchanged, "the sweeper updates these rows in place");
    }

    [Fact]
    public async Task ListByProject_FiltersByRequesterAndStatus_NewestFirst()
    {
        // AuditableEntityInterceptor stamps CreatedAt from the clock, so the clock is moved between saves.
        var older = NewRequest(Guid.NewGuid());
        var newer = NewRequest(Guid.NewGuid());
        var someoneElse = NewRequest(Guid.NewGuid(), requestedBy: Guid.NewGuid());
        var decided = NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Approved);
        _clock.UtcNow = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.AddRange(older, someoneElse, decided);
            await db.SaveChangesAsync();
        }
        _clock.UtcNow = _clock.UtcNow.AddHours(2);
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(newer);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var repo = new EfWorkApprovalRequestRepository(read);
        var mine = await repo.ListByProjectAsync(TenantId, ProjectId, Requester, WorkApprovalRequestStatuses.Pending);
        mine.Select(r => r.Id).Should().Equal(newer.Id, older.Id);

        var all = await repo.ListByProjectAsync(TenantId, ProjectId, null, null);
        all.Should().HaveCount(4);
    }

    [Fact]
    public async Task RevertColumns_RoundTripThroughSaveAndReload()
    {
        var request = NewRequest(Guid.NewGuid(), WorkApprovalRequestStatuses.Pending);
        await using (var db = CreateContext())
        {
            db.WorkApprovalRequests.Add(request);
            await db.SaveChangesAsync();
        }

        var reverter = Guid.NewGuid();
        var revertedAt = DateTimeOffset.UtcNow;
        await using (var db = CreateContext())
        {
            var repo = new EfWorkApprovalRequestRepository(db);
            var tracked = await repo.GetTrackedByIdForTenantAsync(TenantId, request.Id);
            tracked!.UndoStateJson = "{\"title\":\"Old title\"}";
            tracked.RevertedAt = revertedAt;
            tracked.RevertedByEmployeeId = reverter;
            repo.Update(tracked);
            await db.SaveChangesAsync();
        }

        await using var read = CreateContext();
        var reloaded = await new EfWorkApprovalRequestRepository(read).GetTrackedByIdForTenantAsync(TenantId, request.Id);
        reloaded!.UndoStateJson.Should().Be("{\"title\":\"Old title\"}");
        reloaded.RevertedAt.Should().BeCloseTo(revertedAt, TimeSpan.FromSeconds(1));
        reloaded.RevertedByEmployeeId.Should().Be(reverter);
    }

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
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
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
