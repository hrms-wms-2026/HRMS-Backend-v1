namespace ONEVO.Application.Features.Dashboard.Team.DTOs;

/// <summary>My Team capability discovery (spec §7.2) - called every time /dashboard renders, so
/// every flag here is a cheap existence probe, never a scope expansion (§7.3).</summary>
public sealed record MyTeamCapabilitiesResponse(
    bool IsAvailable,
    bool CanViewPeopleStatus,
    bool CanReviewPeopleApprovals,
    bool CanReviewExceptions,
    bool HasWorkApprovals,
    bool LeadsWork,
    bool CanViewLiveActivity,
    Guid? LegalEntityId);
