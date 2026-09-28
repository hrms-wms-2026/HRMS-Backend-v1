using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

namespace ONEVO.Application.Features.CoreHr.Employee.Services;

public readonly record struct BulkItemOutcome(bool Success, bool PendingApproval, string? Error);

/// <summary>
/// Shared loop for employee bulk actions: each id is processed sequentially through the
/// existing single-employee command (so every single-item rule applies), outcomes are mapped
/// per item, and the change tracker is cleared after every item so a failed item's tracked
/// changes can never be flushed by a later item's SaveChangesAsync.
/// </summary>
public static class BulkEmployeeActionRunner
{
    public const int MaxEmployees = 100;
    public const string UnexpectedFailureReason = "Unexpected error while processing this employee.";

    public static async Task<BulkEmployeeActionResponse> RunAsync(
        IEnumerable<Guid> employeeIds,
        Func<Guid, CancellationToken, Task<BulkItemOutcome>> processOne,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken ct)
    {
        var items = new List<BulkEmployeeActionItemResponse>();
        foreach (var employeeId in employeeIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await processOne(employeeId, ct);
                items.Add(outcome.Success
                    ? new BulkEmployeeActionItemResponse(employeeId,
                        outcome.PendingApproval ? BulkEmployeeActionOutcomes.PendingApproval : BulkEmployeeActionOutcomes.Succeeded,
                        null)
                    : new BulkEmployeeActionItemResponse(employeeId, BulkEmployeeActionOutcomes.Failed, outcome.Error));
            }
            catch (FluentValidation.ValidationException ex)
            {
                // ValidationBehavior throws (it does not return a failed Result) - surface the
                // inner command's validation messages as this item's reason.
                items.Add(new BulkEmployeeActionItemResponse(employeeId, BulkEmployeeActionOutcomes.Failed,
                    string.Join(" ", ex.Errors.Select(e => e.ErrorMessage))));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Bulk employee action failed for employee {EmployeeId}", employeeId);
                items.Add(new BulkEmployeeActionItemResponse(employeeId, BulkEmployeeActionOutcomes.Failed, UnexpectedFailureReason));
            }
            finally
            {
                unitOfWork.ClearTracking();
            }
        }

        return BulkEmployeeActionResponse.From(items);
    }
}
