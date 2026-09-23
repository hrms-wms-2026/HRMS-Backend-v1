namespace ONEVO.Api.Contracts.Leave.Policies;

public record CloneLeavePolicyRequest(
    string Name,
    string Country,
    IReadOnlyList<Guid> LegalEntityIds,
    DateOnly EffectiveFrom,
    bool ConfirmReplaceExistingLegalEntityAssignments);
