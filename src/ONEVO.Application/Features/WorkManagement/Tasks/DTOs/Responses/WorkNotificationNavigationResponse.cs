namespace ONEVO.Application.Features.WorkManagement.Tasks.DTOs.Responses;

/// <summary>Target route pieces for in-app notification click-through (frontend spec §5).</summary>
public sealed record WorkNotificationNavigationResponse(
    Guid ProjectId,
    Guid ObjectiveId,
    Guid? TaskId,
    string TargetTab);
