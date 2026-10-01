namespace ONEVO.Application.Features.WorkManagement.Notifications.DTOs;

public sealed record WorkNotificationLogResponse(
    Guid Id,
    string Kind,
    string ActionType,
    string ActionLabel,
    string TargetType,
    Guid? TargetId,
    string TargetTitle,
    Guid ActorEmployeeId,
    string ActorName,
    Guid? ApprovalRequestId,
    DateTimeOffset CreatedAt);
