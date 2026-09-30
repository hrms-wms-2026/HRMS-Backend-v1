namespace ONEVO.Application.Features.CoreHr.Employee.DTOs.Responses;

public static class BulkEmployeeActionOutcomes
{
    public const string Succeeded = "succeeded";
    public const string PendingApproval = "pendingApproval";
    public const string Failed = "failed";
}

public sealed record BulkEmployeeActionItemResponse(Guid EmployeeId, string Outcome, string? Reason);

public sealed record BulkEmployeeActionResponse(
    IReadOnlyList<BulkEmployeeActionItemResponse> Items,
    int Succeeded,
    int PendingApproval,
    int Failed)
{
    public static BulkEmployeeActionResponse From(IReadOnlyList<BulkEmployeeActionItemResponse> items) => new(
        items,
        items.Count(i => i.Outcome == BulkEmployeeActionOutcomes.Succeeded),
        items.Count(i => i.Outcome == BulkEmployeeActionOutcomes.PendingApproval),
        items.Count(i => i.Outcome == BulkEmployeeActionOutcomes.Failed));
}
