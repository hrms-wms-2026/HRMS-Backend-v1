namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record BulkOnboardingBatchRowDetailViewModel(
    int RowNumber, string Status, string? ErrorMessage, Guid? OnboardingDraftId);

public sealed record BulkOnboardingBatchDetailViewModel(
    Guid Id, string Status, int TotalRows, int? ValidRows, int? InvalidRows,
    IReadOnlyList<BulkOnboardingBatchRowDetailViewModel> Rows);
