using ONEVO.Application.Common.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record BulkTaskActionItemResponse(Guid TaskId, string Outcome, string? Reason);

/// <summary>PendingApproval: items sent for approval instead of applied (only bulk delete today).</summary>
public sealed record BulkTaskActionResponse(
    IReadOnlyList<BulkTaskActionItemResponse> Items,
    int Succeeded,
    int Failed,
    int PendingApproval = 0)
{
    public static BulkTaskActionResponse From(IReadOnlyList<BulkItemResult> results) => new(
        results.Select(r => new BulkTaskActionItemResponse(r.Id, r.Outcome, r.Reason)).ToList(),
        results.Count(r => r.Outcome == BulkItemOutcomes.Succeeded),
        results.Count(r => r.Outcome == BulkItemOutcomes.Failed),
        results.Count(r => r.Outcome == BulkItemOutcomes.PendingApproval));
}
