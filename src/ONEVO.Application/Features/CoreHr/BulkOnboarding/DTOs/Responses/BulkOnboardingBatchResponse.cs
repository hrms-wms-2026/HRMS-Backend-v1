namespace ONEVO.Application.Features.CoreHr.BulkOnboarding.DTOs.Responses;

public sealed record BulkOnboardingBatchResponse(
    Guid Id,
    string Status,
    int TotalRows,
    int? ValidRows,
    int? InvalidRows,
    IReadOnlyList<string> DetectedColumns,
    IReadOnlyDictionary<string, string?> SuggestedMapping);
