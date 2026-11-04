namespace ONEVO.Application.Features.Dashboard.Team.DTOs;

/// <summary>My Team capability discovery (spec §7.2) - called every time /dashboard renders, so
/// every flag here is a cheap existence probe, never a scope expansion (§7.3). HasReports = at least one direct report
/// in the reporting line (Today's Team Schedule, 2026-10-29).</summary>
public sealed record MyTeamCapabilitiesResponse(
    bool IsAvailable,
    bool CanViewPeopleStatus,
    bool CanReviewPeopleApprovals,
    bool CanReviewExceptions,
    bool HasWorkApprovals,
    bool LeadsWork,
    bool CanViewLiveActivity,
    bool HasReports,
    Guid? LegalEntityId,
    // Team planner (2026-11-03): the planner is on and the caller has at least one direct report.
    bool CanViewTeamPlan = false);
