using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Api.Contracts.SharedPlatform.Notifications;

public sealed record NotificationDestinationMetadata(
    string NotificationType,
    Guid AttendanceCorrectionId,
    Guid? LegalEntityId,
    string? DestinationKey,
    bool IsNavigable);

public sealed record NotificationViewModel(
    Guid Id, string TemplateCode, string Title, string Body,
    string? RelatedEntityType, Guid? RelatedEntityId, bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt,
    NotificationDestinationMetadata? Destination = null);

public static class NotificationViewModelMapper
{
    public static NotificationViewModel ToViewModel(this NotificationResponse dto) => new(
        dto.Id, dto.TemplateCode, dto.Title, dto.Body,
        dto.RelatedEntityType, dto.RelatedEntityId, dto.IsRead, dto.ReadAt, dto.CreatedAt,
        ResolveDestination(dto));

    private static NotificationDestinationMetadata? ResolveDestination(NotificationResponse dto)
    {
        if (!string.Equals(dto.RelatedEntityType, "attendance_correction", StringComparison.OrdinalIgnoreCase)
            || dto.RelatedEntityId is not Guid correctionId)
            return null;

        var isApprovalRequest = string.Equals(
            dto.TemplateCode, "attendance_correction_request_created", StringComparison.OrdinalIgnoreCase);
        return new NotificationDestinationMetadata(
            dto.TemplateCode,
            correctionId,
            LegalEntityId: null,
            isApprovalRequest ? "attendance_correction_approval" : null,
            isApprovalRequest);
    }
}
