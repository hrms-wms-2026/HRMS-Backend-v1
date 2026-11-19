namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveEditOutcomeResponse(bool Applied, ObjectiveDetailResponse? Objective, Guid? ApprovalRequestId);
