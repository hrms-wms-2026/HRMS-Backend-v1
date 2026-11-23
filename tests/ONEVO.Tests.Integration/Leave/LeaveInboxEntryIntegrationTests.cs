using Microsoft.EntityFrameworkCore;
using ONEVO.Application.Features.Leave.Approval.RepositoryInterfaces;
using ONEVO.Application.Features.Leave.Inbox;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.Inbox.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Repositories.Inbox;
using ONEVO.Tests.Integration.MyTeam;
using Xunit;

namespace ONEVO.Tests.Integration.Leave;

/// <summary>
/// Task 37 on SQL Server, as the app login under row-level security: a leave request created, decided and reverted
/// moves its inbox_items header in the same SaveChanges, a rolled-back change leaves the header untouched, and the
/// badge count is one query on inbox_item_assignees. Requires Docker.
/// </summary>
public sealed class LeaveInboxEntryIntegrationTests
{
    private static async Task<(MyTeamDb Db, Guid RequestId, Guid First, Guid Second)> SeedAsync()
    {
        var helper = await MyTeamDb.CreateAsync();
        await using var db = helper.NewContext();
        var requester = helper.AddEmployee(db, lastName: "Requester");
        var first = helper.AddEmployee(db, lastName: "First");
        var second = helper.AddEmployee(db, lastName: "Second");
        var type = helper.AddLeaveType(db);
        var request = helper.AddApprovedLeave(db, requester.Id, type, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 2));
        request.Status = LeaveRequestStatuses.Pending;
        request.ApprovedAt = null;
        await db.SaveChangesAsync();
        return (helper, request.Id, first.Id, second.Id);
    }

    private static LeaveInboxEntrySync Sync(ApplicationDbContext db)
        => new(new EfInboxEntryStore(db), approvals: null!); // the state overload never reloads

    private static async Task<LeaveApprovalState> LoadAsync(ApplicationDbContext db, Guid requestId)
    {
        var request = await db.LeaveRequests.SingleAsync(r => r.Id == requestId);
        var employee = await db.Employees.AsNoTracking().SingleAsync(e => e.Id == request.EmployeeId);
        var approvers = await db.LeaveRequestApprovers.Where(a => a.LeaveRequestId == requestId).OrderBy(a => a.SequenceOrder).ToListAsync();
        return new LeaveApprovalState(request, null, employee, "Annual", "AN", LeaveApprovalModes.InOrder, approvers);
    }

    private static async Task<InboxEntry?> HeaderAsync(MyTeamDb helper, Guid requestId)
    {
        await using var db = helper.NewTenantContext();
        return await db.InboxEntries.AsNoTracking().Include(e => e.Assignees)
            .SingleOrDefaultAsync(e => e.Source == LeaveInboxKinds.Source && e.Kind == LeaveInboxKinds.Request && e.SubjectId == requestId);
    }

    private static string Status(InboxEntry entry, Guid employeeId) => entry.Assignees.Single(a => a.EmployeeId == employeeId).Status;

    [Fact]
    public async Task Create_Decide_Revert_MoveTheHeaderInTheSameSave()
    {
        var (helper, requestId, first, second) = await SeedAsync();

        // Created: the approver rows and the header land in one SaveChanges.
        await using (var db = helper.NewTenantContext())
        {
            db.LeaveRequestApprovers.AddRange(
                new LeaveRequestApprover { Id = Guid.NewGuid(), TenantId = helper.TenantId, LeaveRequestId = requestId, ApproverEmployeeId = first, SequenceOrder = 1 },
                new LeaveRequestApprover { Id = Guid.NewGuid(), TenantId = helper.TenantId, LeaveRequestId = requestId, ApproverEmployeeId = second, SequenceOrder = 2 });
            var state = await LoadAsync(db, requestId);
            state = state with { Approvers = db.ChangeTracker.Entries<LeaveRequestApprover>().Select(e => e.Entity).OrderBy(a => a.SequenceOrder).ToList() };
            await Sync(db).StageAsync(state, default);
            await db.SaveChangesAsync();
        }
        var created = (await HeaderAsync(helper, requestId))!;
        Assert.Equal(InboxEntryStatuses.Pending, created.Status);
        Assert.Equal(InboxEntryAssigneeStatuses.Pending, Status(created, first));
        Assert.Equal(InboxEntryAssigneeStatuses.Waiting, Status(created, second));

        // First approver decides: the second must act now.
        await using (var db = helper.NewTenantContext())
        {
            var state = await LoadAsync(db, requestId);
            var row = state.Approvers.Single(a => a.ApproverEmployeeId == first);
            row.Status = LeaveRequestApproverStatuses.Approved;
            row.DecidedAt = DateTimeOffset.UtcNow;
            await Sync(db).StageAsync(state, default);
            await db.SaveChangesAsync();

            var counts = await new EfInboxEntryStore(db).CountPendingForAsync(helper.TenantId, second, [LeaveInboxKinds.Source], default);
            Assert.Equal(1, counts[(LeaveInboxKinds.Source, LeaveInboxKinds.Request)]);
            Assert.Empty(await new EfInboxEntryStore(db).CountPendingForAsync(helper.TenantId, first, [LeaveInboxKinds.Source], default));
        }
        var decided = (await HeaderAsync(helper, requestId))!;
        Assert.Equal(InboxEntryAssigneeStatuses.Approved, Status(decided, first));
        Assert.Equal(InboxEntryAssigneeStatuses.Pending, Status(decided, second));

        // Reverted: the first approver's decision undone, the chain is back where it started.
        await using (var db = helper.NewTenantContext())
        {
            var state = await LoadAsync(db, requestId);
            var row = state.Approvers.Single(a => a.ApproverEmployeeId == first);
            row.Status = LeaveRequestApproverStatuses.Pending;
            row.DecidedAt = null;
            await Sync(db).StageAsync(state, default);
            await db.SaveChangesAsync();
        }
        var reverted = (await HeaderAsync(helper, requestId))!;
        Assert.Equal(created.Id, reverted.Id);
        Assert.Equal(InboxEntryAssigneeStatuses.Pending, Status(reverted, first));
        Assert.Equal(InboxEntryAssigneeStatuses.Waiting, Status(reverted, second));
    }

    [Fact]
    public async Task A_RolledBack_Change_LeavesTheHeaderAsItWas()
    {
        var (helper, requestId, first, _) = await SeedAsync();
        await using (var db = helper.NewTenantContext())
        {
            db.LeaveRequestApprovers.Add(new LeaveRequestApprover
            {
                Id = Guid.NewGuid(), TenantId = helper.TenantId, LeaveRequestId = requestId, ApproverEmployeeId = first, SequenceOrder = 1,
            });
            await db.SaveChangesAsync();
            await Sync(db).StageAsync(await LoadAsync(db, requestId), default);
            await db.SaveChangesAsync();
        }

        await using (var db = helper.NewTenantContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var state = await LoadAsync(db, requestId);
            state.Request.Status = LeaveRequestStatuses.Rejected;
            state.Approvers.Single().Status = LeaveRequestApproverStatuses.Rejected;
            await Sync(db).StageAsync(state, default);
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        var header = (await HeaderAsync(helper, requestId))!;
        Assert.Equal(InboxEntryStatuses.Pending, header.Status);
        Assert.Equal(InboxEntryAssigneeStatuses.Pending, Status(header, first));
        await using var check = helper.NewTenantContext();
        Assert.Equal(LeaveRequestStatuses.Pending, (await check.LeaveRequests.AsNoTracking().SingleAsync(r => r.Id == requestId)).Status);
    }

    [Fact]
    public async Task Another_Tenant_Cannot_See_The_Header()
    {
        var (helper, requestId, first, _) = await SeedAsync();
        await using (var db = helper.NewTenantContext())
        {
            db.LeaveRequestApprovers.Add(new LeaveRequestApprover
            {
                Id = Guid.NewGuid(), TenantId = helper.TenantId, LeaveRequestId = requestId, ApproverEmployeeId = first, SequenceOrder = 1,
            });
            await db.SaveChangesAsync();
            await Sync(db).StageAsync(await LoadAsync(db, requestId), default);
            await db.SaveChangesAsync();
        }
        var otherTenant = (await helper.AddTenantAsync()).TenantId;

        await using var other = helper.NewTenantContext(otherTenant);
        Assert.Equal(0, await other.InboxEntries.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await other.InboxEntryAssignees.IgnoreQueryFilters().CountAsync());
    }
}
