namespace ONEVO.Api.Contracts.CoreHr.BulkOnboarding;

public sealed record PreviewBulkOnboardingMappingRequest(IReadOnlyDictionary<string, string?> Mapping);
