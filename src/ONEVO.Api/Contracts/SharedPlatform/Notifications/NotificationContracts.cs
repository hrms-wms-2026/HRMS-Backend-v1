using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Api.Contracts.SharedPlatform.Notifications;

public sealed record NotificationViewModel(
    Guid Id, string TemplateCode, string Title, string Body,
    string? RelatedEntityType, Guid? RelatedEntityId, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt);

public static class NotificationViewModelMapper
{
    public static NotificationViewModel ToViewModel(this NotificationResponse dto) => new(
        dto.Id, dto.TemplateCode, dto.Title, dto.Body,
        dto.RelatedEntityType, dto.RelatedEntityId, dto.IsRead, dto.ReadAt, dto.CreatedAt);
}
