namespace ONEVO.Api.Contracts.WorkManagement.Objectives;

/// <summary>Title/Description/StartDate/EndDate/AllocatedHours are only used for an Edit-type
/// request, to let the approver adjust the requested fields before approving. Provide all four
/// of Title/StartDate/EndDate/AllocatedHours together to override, or omit them all to approve
/// the request exactly as submitted.</summary>
public sealed record ApproveObjectiveChangeRequestRequest(
    decimal? ApprovedAdditionalHours,
    string? Title = null,
    string? Description = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    decimal? AllocatedHours = null);
