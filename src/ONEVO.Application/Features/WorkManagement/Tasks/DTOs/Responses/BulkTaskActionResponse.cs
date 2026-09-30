using ONEVO.Application.Common.Services;

namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

public sealed record BulkTaskActionItemResponse(Guid TaskId, string Outcome, string? Reason);

public sealed record BulkTaskActionResponse(
    IReadOnlyList<BulkTaskActionItemResponse> Items,
    int Succeeded,
    int Failed)
{
    public static BulkTaskActionResponse From(IReadOnlyList<BulkItemResult> results) => new(
        results.Select(r => new BulkTaskActionItemResponse(r.Id, r.Outcome, r.Reason)).ToList(),
        results.Count(r => r.Outcome == BulkItemOutcomes.Succeeded),
        results.Count(r => r.Outcome == BulkItemOutcomes.Failed));
}
