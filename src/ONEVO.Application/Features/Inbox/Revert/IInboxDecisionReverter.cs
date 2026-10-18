using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;

namespace ONEVO.Application.Features.Inbox.Revert;

/// <summary>Undoes the latest decision on one (source, kind). The router runs it for the generic "revert" action
/// when the kind's descriptor declares <see cref="InboxCapabilities.Revert"/>.</summary>
public interface IInboxDecisionReverter
{
    string Source { get; }
    string Kind { get; }

    Task<Result> CanRevertAsync(Guid tenantId, ItemRef item, Guid callerEmployeeId, CancellationToken ct);

    Task<Result<InboxActionResult>> RevertAsync(Guid tenantId, ItemRef item, Guid callerEmployeeId, string? comment, CancellationToken ct);
}
