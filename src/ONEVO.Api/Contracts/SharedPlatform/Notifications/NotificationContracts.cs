using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Api.Contracts.SharedPlatform.Notifications;

public sealed record NotificationDestinationMetadata
{
    public required string NotificationType { get; init; }

    // Retained for the existing Attendance Correction JSON contract.
    public Guid? AttendanceCorrectionId { get; init; }

    public Guid? WorkAreaChangeRequestId { get; init; }

    public Guid? LegalEntityId { get; init; }

    public string? DestinationKey { get; init; }

    public bool IsNavigable { get; init; }
}

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
        if (dto.RelatedEntityId is not Guid relatedId)
            return null;

        var isCorrection = string.Equals(dto.RelatedEntityType, "attendance_correction", StringComparison.OrdinalIgnoreCase);
        var isWorkArea = string.Equals(dto.RelatedEntityType, "work_area_change_request", StringComparison.OrdinalIgnoreCase);
        if (!isCorrection && !isWorkArea)
            return null;

        var isApprovalRequest = string.Equals(
            dto.TemplateCode,
            isCorrection ? "attendance_correction_request_created" : "work_area_change_request_created",
            StringComparison.OrdinalIgnoreCase);

        return new NotificationDestinationMetadata
        {
            NotificationType = dto.TemplateCode,
            AttendanceCorrectionId = isCorrection ? relatedId : null,
            WorkAreaChangeRequestId = isWorkArea ? relatedId : null,
            LegalEntityId = null,
            DestinationKey = isApprovalRequest
                ? (isCorrection ? "attendance_correction_approval" : "work_area_change_approval")
                : null,
            IsNavigable = isApprovalRequest
        };
    }
}
