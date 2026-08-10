using ONEVO.Application.Features.WorkManagement.ObjectiveChangeRequests.DTOs.Responses;

namespace ONEVO.Application.Features.WorkManagement.Objectives.DTOs.Responses;

public sealed record ObjectiveChangeOutcomeResponse(bool Applied, ObjectiveChangeRequestResponse? PendingRequest);
