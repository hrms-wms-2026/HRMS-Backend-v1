using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Domain.Features.Inbox.Entities;

namespace ONEVO.Application.Features.Inbox.Revert;

/// <summary>
/// The standard revert rules (plan README §3.4 rule 1) for every reverter: only the journal row's actor, only
/// within <see cref="InboxRevertWindow.Minutes"/>, only once, and only the item's latest decision. The module
/// half - the dependency check and the rollback itself - is the subclass's. The rollback and the "revert"
/// journal row land in one transaction.
/// </summary>
public abstract class InboxDecisionReverterBase : IInboxDecisionReverter
{
    public const string NothingToRevert = "There is no decision to undo on this item.";
    public const string NotYourDecision = "Only the person who made this decision can undo it.";
    public const string WindowClosed = "The 30-minute undo window for this decision has closed.";
    public const string AlreadyReverted = "This decision has already been undone.";

    private readonly IInboxDecisionJournal _journal;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    protected InboxDecisionReverterBase(IInboxDecisionJournal journal, IUnitOfWork unitOfWork, IDateTimeProvider clock)
    {
        _journal = journal;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public abstract string Source { get; }
    public abstract string Kind { get; }

    /// <summary>Module rule: is anything now depending on this decision? Failure blocks the revert (409/422).</summary>
    protected abstract Task<Result> CheckDependenciesAsync(Guid tenantId, InboxDecisionJournalEntry decision, CancellationToken ct);

    /// <summary>Rolls the module back. Runs inside the revert transaction; the journal row is written after it.</summary>
    protected abstract Task<Result<InboxActionResult>> ApplyRevertAsync(
        Guid tenantId, InboxDecisionJournalEntry decision, Guid callerEmployeeId, CancellationToken ct);

    public async Task<Result> CanRevertAsync(Guid tenantId, ItemRef item, Guid callerEmployeeId, CancellationToken ct)
    {
        var (decision, error) = await RevertableDecisionAsync(tenantId, item, callerEmployeeId, ct);
        return decision is null ? error! : await CheckDependenciesAsync(tenantId, decision, ct);
    }

    public async Task<Result<InboxActionResult>> RevertAsync(
        Guid tenantId, ItemRef item, Guid callerEmployeeId, string? comment, CancellationToken ct)
    {
        var (decision, error) = await RevertableDecisionAsync(tenantId, item, callerEmployeeId, ct);
        if (decision is null)
            return Result<InboxActionResult>.Failure(error!.Error!, error.StatusCode ?? 400);

        var dependencies = await CheckDependenciesAsync(tenantId, decision, ct);
        if (!dependencies.IsSuccess)
            return Result<InboxActionResult>.Failure(dependencies.Error!, dependencies.StatusCode ?? 409);

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                var applied = await ApplyRevertAsync(tenantId, decision, callerEmployeeId, token);
                if (!applied.IsSuccess)
                    throw new InboxRevertAbortedException(applied);

                var revert = new InboxDecisionJournalEntry
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Source = decision.Source, Kind = decision.Kind, ItemId = decision.ItemId,
                    Action = InboxJournalActions.Revert, ActorEmployeeId = callerEmployeeId, BeforeJson = decision.AfterJson,
                    AfterJson = decision.BeforeJson, Comment = comment, DecidedAt = _clock.UtcNow,
                };
                // A concurrent revert (double-clicked Undo) claimed the decision first: roll this one back.
                if (!await _journal.MarkRevertedAsync(decision, revert, token))
                    throw new InboxRevertAbortedException(Result<InboxActionResult>.Conflict(AlreadyReverted));
                return applied;
            }, ct);
        }
        catch (InboxRevertAbortedException aborted)
        {
            return aborted.Result;
        }
    }

    private async Task<(InboxDecisionJournalEntry? Decision, Result? Error)> RevertableDecisionAsync(
        Guid tenantId, ItemRef item, Guid callerEmployeeId, CancellationToken ct)
    {
        var latest = await _journal.LatestAsync(tenantId, item, ct);
        if (latest is null)
            return (null, Result.NotFound(NothingToRevert));
        if (latest.Action == InboxJournalActions.Revert || latest.RevertedByJournalId is not null)
            return (null, Result.Conflict(AlreadyReverted));
        if (latest.ActorEmployeeId != callerEmployeeId)
            return (null, Result.Forbidden(NotYourDecision));
        if (_clock.UtcNow > InboxRevertWindow.Until(latest))
            return (null, Result.Conflict(WindowClosed));
        return (latest, null);
    }

    /// <summary>Rolls the transaction back when the module refuses the rollback, carrying its result out.</summary>
    private sealed class InboxRevertAbortedException(Result<InboxActionResult> result) : Exception(result.Error)
    {
        public Result<InboxActionResult> Result { get; } = result;
    }
}
