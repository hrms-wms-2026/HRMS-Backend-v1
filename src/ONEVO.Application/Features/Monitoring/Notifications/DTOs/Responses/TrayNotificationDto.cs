namespace ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;

public record TrayNotificationDto(Guid Id, string Type, string Title, string Message);

public record NotificationInboxItemDto(
    Guid Id, string Type, string Title, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
