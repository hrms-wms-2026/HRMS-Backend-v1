using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.Services;
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
        var results = await BulkItemRunner.RunAsync(
            employeeIds, processOne, unitOfWork, logger, UnexpectedFailureReason, ct);
        return BulkEmployeeActionResponse.From(results
            .Select(r => new BulkEmployeeActionItemResponse(r.Id, r.Outcome, r.Reason))
            .ToList());
    }
}
