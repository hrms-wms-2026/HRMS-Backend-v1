using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.Conversations.Entities;
using ONEVO.Domain.Features.Leave.BalanceAudit.Entities;
using ONEVO.Domain.Features.Leave.Common;
using ONEVO.Domain.Features.Leave.Request.Entities;
using ONEVO.Domain.Features.Leave.Type.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class DapiHistorySeeder
{
    private sealed record LeavePlan(Guid EmployeeId, DateOnly Date, Guid LeaveTypeId, string Status, string Reason);

    private static readonly string[] LeaveReasons =
    [
        "Family function in Jaffna",
        "Medical appointment",
        "Personal errands",
        "Child's school event",
        "Feeling unwell",
        "Visiting parents",
        "House moving",
        "Religious festival"
    ];

    /// <summary>
    /// Per person: two past full days off (mostly approved, some rejected) and, for every third person, one upcoming
    /// request still waiting on their approver - so approvers, the owner included, have live requests to decide.
    /// Pure: the same window always gives the same plan.
    /// </summary>
    private static List<LeavePlan> PlanLeave(SeedContext ctx, IReadOnlyList<LeaveType> leaveTypes)
    {
        var plans = new List<LeavePlan>();
        if (leaveTypes.Count == 0)
            return plans;

        var workdays = ctx.Window.PastWorkdays();
        if (workdays.Count < 4)
            return plans;

        for (var index = 0; index < ctx.Employees.Count; index++)
        {
            var employee = ctx.Employees[index];
            var isOwner = employee.Id == ctx.Owner.Id;
            var first = workdays[(3 + index * 5) % workdays.Count];
            var second = workdays[(21 + index * 7) % workdays.Count];
            if (second == first)
                second = workdays[(22 + index * 7) % workdays.Count];

            plans.Add(new LeavePlan(employee.Id, first, leaveTypes[index % leaveTypes.Count].Id,
                !isOwner && index % 6 == 1 ? LeaveRequestStatuses.Rejected : LeaveRequestStatuses.Approved,
                LeaveReasons[index % LeaveReasons.Length]));
            plans.Add(new LeavePlan(employee.Id, second, leaveTypes[(index + 1) % leaveTypes.Count].Id,
                !isOwner && index % 5 == 2 ? LeaveRequestStatuses.Rejected : LeaveRequestStatuses.Approved,
                LeaveReasons[(index + 3) % LeaveReasons.Length]));

            if (!isOwner && index % 3 == 0)
            {
                var upcoming = ctx.Window.Today.AddDays(4 + index % 9);
                while (!IsWorkday(upcoming))
                    upcoming = upcoming.AddDays(1);
                plans.Add(new LeavePlan(employee.Id, upcoming, leaveTypes[(index + 2) % leaveTypes.Count].Id,
                    LeaveRequestStatuses.Pending, LeaveReasons[(index + 5) % LeaveReasons.Length]));
            }
        }
        return plans;
    }

    private static async Task SeedLeaveAsync(SeedContext ctx, IReadOnlyList<LeavePlan> plans, CancellationToken ct)
    {
        if (plans.Count == 0)
            return;
        var db = ctx.Db;
        var requestIds = plans.Select(p => LeaveRequestId(p)).ToList();
        var existingRequestIds = await ExistingIdsAsync(db.LeaveRequests.Where(x => requestIds.Contains(x.Id)).Select(x => x.Id), ct);
        var approverIds = requestIds.Select(id => SeedId($"leave-approver:{id:N}")).ToList();
        var existingApproverIds = await ExistingIdsAsync(db.LeaveRequestApprovers.Where(x => approverIds.Contains(x.Id)).Select(x => x.Id), ct);
        var allocationIds = requestIds.Select(id => SeedId($"leave-allocation:{id:N}")).ToList();
        var existingAllocationIds = await ExistingIdsAsync(
            db.LeaveRequestDayAllocations.Where(x => allocationIds.Contains(x.Id)).Select(x => x.Id), ct);
        var auditIds = requestIds.Select(id => SeedId($"leave-deduction:{id:N}")).ToList();
        var existingAuditIds = await ExistingIdsAsync(db.LeaveBalanceAudits.Where(x => auditIds.Contains(x.Id)).Select(x => x.Id), ct);

        foreach (var plan in plans)
        {
            var approverId = await ResolveLeaveApproverAsync(ctx, plan.EmployeeId, plan.Date, ct);
            await SeedLeaveRequestAsync(
                db, plan, approverId, existingRequestIds, existingApproverIds, existingAllocationIds, existingAuditIds, ct);
        }
        await db.SaveChangesAsync(ct);
    }

    private static Guid LeaveRequestId(LeavePlan plan) =>
        SeedId($"leave-request:{plan.EmployeeId:N}:{plan.Date:yyyy-MM-dd}");

    /// <summary>Who the leave route picks for this employee's leave on that day; their manager (else the owner) when it
    /// finds nobody.</summary>
    private static async Task<Guid> ResolveLeaveApproverAsync(SeedContext ctx, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        if (ctx.Approvers is not null)
        {
            var resolution = await ctx.Approvers.ResolveAsync(DapiTenantId, employeeId, date, date, ct);
            var first = resolution.Approvers.OrderBy(a => a.SequenceOrder).Select(a => (Guid?)a.ApproverEmployeeId).FirstOrDefault();
            if (first is Guid found && found != employeeId)
                return found;
        }
        return ctx.ManagerOrOwner(employeeId);
    }

    private static async Task SeedLeaveRequestAsync(
        ApplicationDbContext db,
        LeavePlan plan,
        Guid approverEmployeeId,
        HashSet<Guid> existingRequestIds,
        HashSet<Guid> existingApproverIds,
        HashSet<Guid> existingAllocationIds,
        HashSet<Guid> existingAuditIds,
        CancellationToken ct)
    {
        var requestId = LeaveRequestId(plan);
        var requestIsNew = existingRequestIds.Add(requestId);
        var submittedAt = plan.Status == LeaveRequestStatuses.Pending
            ? At(plan.Date.AddDays(-6), 10, 15)
            : At(plan.Date.AddDays(-5), 10, 0);
        var decidedAt = submittedAt.AddDays(1).AddHours(-4);

        if (requestIsNew)
        {
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = requestId,
                TenantId = DapiTenantId,
                EmployeeId = plan.EmployeeId,
                LeaveTypeId = plan.LeaveTypeId,
                StartAt = At(plan.Date, 9, 0),
                EndAt = At(plan.Date, 18, 0),
                TotalHours = 8m,
                PaidHours = 8m,
                UnpaidHours = 0m,
                Reason = plan.Reason,
                Status = plan.Status,
                ApprovedBy = plan.Status == LeaveRequestStatuses.Approved ? approverEmployeeId : null,
                ApprovedAt = plan.Status == LeaveRequestStatuses.Approved ? decidedAt : null,
                ConflictSnapshotJson = "{}",
                NoticePeriodMissed = false,
                CreatedAt = submittedAt,
                UpdatedAt = plan.Status == LeaveRequestStatuses.Pending ? submittedAt : decidedAt
            });
        }

        var approverRowId = SeedId($"leave-approver:{requestId:N}");
        if (!existingApproverIds.Add(approverRowId))
        {
            await RerouteIfPendingAsync(db, requestId, approverRowId, approverEmployeeId, ct);
        }
        else
        {
            var approverStatus = plan.Status switch
            {
                LeaveRequestStatuses.Approved => LeaveRequestApproverStatuses.Approved,
                LeaveRequestStatuses.Rejected => LeaveRequestApproverStatuses.Rejected,
                _ => LeaveRequestApproverStatuses.Pending
            };
            db.LeaveRequestApprovers.Add(new LeaveRequestApprover
            {
                Id = approverRowId,
                TenantId = DapiTenantId,
                LeaveRequestId = requestId,
                ApproverEmployeeId = approverEmployeeId,
                SequenceOrder = 1,
                Status = approverStatus,
                Comment = approverStatus == LeaveRequestApproverStatuses.Approved
                    ? "Approved. Enjoy your day off."
                    : approverStatus == LeaveRequestApproverStatuses.Rejected
                        ? "Sorry, we have a release that week - please pick another day."
                        : null,
                DecidedAt = approverStatus == LeaveRequestApproverStatuses.Pending ? null : decidedAt
            });
        }

        var allocationId = SeedId($"leave-allocation:{requestId:N}");
        if (existingAllocationIds.Add(allocationId))
        {
            db.LeaveRequestDayAllocations.Add(new LeaveRequestDayAllocation
            {
                Id = allocationId,
                TenantId = DapiTenantId,
                LeaveRequestId = requestId,
                LeaveDate = plan.Date,
                HoursUnit = 8m,
                PaidHoursUnit = 8m,
                UnpaidHoursUnit = 0m,
                Status = LeaveRequestDayAllocationStatuses.Active,
                CreatedAt = submittedAt
            });
        }

        if (!requestIsNew || plan.Status == LeaveRequestStatuses.Rejected)
            return;

        var entitlement = await db.LeaveEntitlements.FirstOrDefaultAsync(
            e => e.TenantId == DapiTenantId
                && e.EmployeeId == plan.EmployeeId
                && e.LeaveTypeId == plan.LeaveTypeId
                && e.Year == plan.Date.Year,
            ct);
        if (entitlement is null)
            return;

        if (plan.Status == LeaveRequestStatuses.Pending)
        {
            entitlement.PendingHours += 8m;
            entitlement.UpdatedAt = submittedAt;
            return;
        }

        entitlement.UsedHours += 8m;
        entitlement.UpdatedAt = decidedAt;
        var auditId = SeedId($"leave-deduction:{requestId:N}");
        if (existingAuditIds.Add(auditId))
        {
            db.LeaveBalanceAudits.Add(new LeaveBalanceAudit
            {
                Id = auditId,
                TenantId = DapiTenantId,
                EmployeeId = plan.EmployeeId,
                LeaveTypeId = plan.LeaveTypeId,
                ChangeType = LeaveBalanceChangeTypes.Deduction,
                HoursChanged = -8m,
                BalanceAfter = entitlement.TotalHours + entitlement.CarriedForwardHours - entitlement.UsedHours,
                Reason = $"Approved leave on {plan.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}",
                RelatedRequestId = requestId,
                CreatedAt = decidedAt,
                CreatedBy = DapiOwnerUserId
            });
        }
    }

    /// <summary>
    /// A demo leave still waiting for its approver moves to whoever the leave route picks now. Its case cards in 1-on-1
    /// chats go too, so the inbox places it in the new approver's chat on their next load. Decided leaves keep their history.
    /// </summary>
    private static async Task RerouteIfPendingAsync(
        ApplicationDbContext db, Guid requestId, Guid approverRowId, Guid approverEmployeeId, CancellationToken ct)
    {
        var row = await db.LeaveRequestApprovers.FirstOrDefaultAsync(a => a.Id == approverRowId, ct);
        if (row is null || row.Status != LeaveRequestApproverStatuses.Pending || row.ApproverEmployeeId == approverEmployeeId)
            return;
        var requestPending = await db.LeaveRequests.AnyAsync(
            r => r.Id == requestId && r.Status == LeaveRequestStatuses.Pending, ct);
        if (!requestPending)
            return;

        row.ApproverEmployeeId = approverEmployeeId;
        row.DelegatedFromApproverId = null;

        var cardRef = $"\"id\":\"{requestId}\"";
        var directChatIds = db.Conversations
            .Where(c => c.TenantId == DapiTenantId && c.SubjectSource == ConversationSources.Direct)
            .Select(c => c.Id);
        var staleCards = await db.ConversationMessages
            .Where(m => m.TenantId == DapiTenantId
                && m.Kind == ConversationMessageKinds.Case
                && directChatIds.Contains(m.ConversationId)
                && m.SystemEventJson != null
                && m.SystemEventJson.Contains(cardRef))
            .ToListAsync(ct);
        if (staleCards.Count == 0)
            return;
        var staleIds = staleCards.Select(m => m.Id).ToList();
        var replies = await db.ConversationMessages
            .Where(m => m.ReplyToMessageId != null && staleIds.Contains(m.ReplyToMessageId.Value))
            .ToListAsync(ct);
        foreach (var reply in replies)
            reply.ReplyToMessageId = null;
        db.ConversationMessages.RemoveRange(staleCards);
    }
}
