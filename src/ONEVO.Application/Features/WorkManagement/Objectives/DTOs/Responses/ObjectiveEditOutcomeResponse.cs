namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveEditOutcomeResponse(
    bool Applied,
    ObjectiveDetailResponse? Objective,
    ObjectiveChangeRequests.DTOs.Responses.ObjectiveChangeRequestResponse? PendingRequest);
