namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record ValidateBulkOnboardingBatchRequest(IReadOnlyDictionary<string, string?> Mapping);

public sealed record ValidateBulkOnboardingBatchResponse(
    int ValidRows, int InvalidRows, IReadOnlyList<BulkOnboardingRowValidationItem> Rows);

public sealed record BulkOnboardingRowValidationItem(int RowNumber, string Status, string? ErrorMessage);
