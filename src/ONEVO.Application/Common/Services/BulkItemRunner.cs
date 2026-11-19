using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.Services;

namespace ONEVO.Application.Common.Services;

public static class BulkItemOutcomes
{
    public const string Succeeded = "succeeded";
    public const string PendingApproval = "pendingApproval";
    public const string Failed = "failed";
}

public sealed record BulkItemResult(Guid Id, string Outcome, string? Reason);

/// <summary>
/// Shared loop for bulk actions: each id is processed sequentially through the existing
/// single-item command (so every single-item rule applies), outcomes are mapped per item, and
/// the change tracker is cleared after every item so a failed item's tracked changes can never
/// be flushed by a later item's SaveChangesAsync.
/// </summary>
public static class BulkItemRunner
{
    public const int MaxItems = 100;

    public static async Task<IReadOnlyList<BulkItemResult>> RunAsync(
        IEnumerable<Guid> ids,
        Func<Guid, CancellationToken, Task<BulkItemOutcome>> processOne,
        IUnitOfWork unitOfWork,
        ILogger logger,
        string unexpectedFailureReason,
        CancellationToken ct)
    {
        var results = new List<BulkItemResult>();
        foreach (var id in ids.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await processOne(id, ct);
                results.Add(outcome.Success
                    ? new BulkItemResult(id, outcome.PendingApproval ? BulkItemOutcomes.PendingApproval : BulkItemOutcomes.Succeeded, null)
                    : new BulkItemResult(id, BulkItemOutcomes.Failed, outcome.Error));
            }
            catch (FluentValidation.ValidationException ex)
            {
                results.Add(new BulkItemResult(id, BulkItemOutcomes.Failed,
                    string.Join(" ", ex.Errors.Select(e => e.ErrorMessage))));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bulk action failed for item {ItemId}", id);
                results.Add(new BulkItemResult(id, BulkItemOutcomes.Failed, unexpectedFailureReason));
            }
            finally
            {
                unitOfWork.ClearTracking();
            }
        }

        return results;
    }
}
