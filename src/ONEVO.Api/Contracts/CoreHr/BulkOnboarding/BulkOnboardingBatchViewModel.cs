namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingBatchViewModel(
    Guid Id,
    string Status,
    int TotalRows,
    int? ValidRows,
    int? InvalidRows,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyDictionary<string, string?> SuggestedMapping);
