namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record FinalizeBulkOnboardingBatchRequest(IReadOnlyList<Guid> OnboardingDraftIds);
