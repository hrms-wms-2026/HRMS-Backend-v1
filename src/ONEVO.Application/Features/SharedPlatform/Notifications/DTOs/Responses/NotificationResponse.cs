namespace ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

public sealed record NotificationResponse(
    Guid Id, string TemplateCode, string Title, string Body,
    string? RelatedEntityType, Guid? RelatedEntityId, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);
