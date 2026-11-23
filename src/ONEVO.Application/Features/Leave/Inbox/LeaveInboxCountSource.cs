using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Leave.Inbox;

/// <summary>
/// Task 37: leave badge counts from inbox_item_assignees in one indexed query, instead of loading every pending request
/// and its approval state. Same rule as the list: only with the permission the approve endpoint requires, and only
/// requests where the caller must act now. Leave has no alerts.
/// </summary>
public sealed class LeaveInboxCountSource(ICurrentUser currentUser, IInboxEntryStore entries) : IInboxHeaderCountSource
{
    public string Source => LeaveInboxKinds.Source;

    public async Task<InboxSourceCounts> CountAsync(Guid tenantId, Guid callerEmployeeId, CancellationToken ct)
    {
        if (!currentUser.HasPermission(LeaveInboxSourceProvider.LeaveApprovePermission))
            return new InboxSourceCounts(0, 0, new Dictionary<string, int>());
        var counts = await entries.CountPendingForAsync(tenantId, callerEmployeeId, [Source], ct);
        var byKind = counts.ToDictionary(c => c.Key.Kind, c => c.Value);
        return new InboxSourceCounts(byKind.Values.Sum(), 0, byKind);
    }
}
