using ONEVO.Application.Features.DevPlatform.Support.DTOs;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Mappers;

public static class SupportMapper
{
    public static SupportTicketDto ToDto(SupportTicket t, string tenantName, string? assignedToName) => new(
        t.Id, t.TenantId, tenantName, t.Subject, t.Description, t.Status, t.Priority, t.Category,
        t.CreatedByPlatformUserId, t.CreatedByUserId, t.CreatedByName,
        t.AssignedToPlatformUserId, assignedToName, t.CreatedAt, t.UpdatedAt, t.ResolvedAt);

    public static SupportTicketCommentDto ToDto(SupportTicketComment c) => new(
        c.Id, c.TicketId, c.AuthorPlatformUserId, c.AuthorUserId, c.AuthorName, c.Body, c.IsInternal, c.CreatedAt);

    public static SupportTicketAttachmentDto ToDto(SupportTicketAttachment a) => new(
        a.Id, a.TicketId, a.FileName, a.ContentType, a.SizeBytes, a.IsInternal,
        a.UploadedByPlatformUserId, a.UploadedByUserId, a.UploadedByName, a.CreatedAt);

    public static SupportTicketDetailDto ToDetailDto(SupportTicket t, string tenantName, string? assignedToName) => new(
        ToDto(t, tenantName, assignedToName),
        t.Comments.OrderBy(c => c.CreatedAt).Select(ToDto).ToList(),
        t.Attachments.OrderBy(a => a.CreatedAt).Select(ToDto).ToList());

    public static TenantSupportTicketDto ToTenantDto(SupportTicket t) => new(
        t.Id, t.Subject, t.Description, t.Status, t.Priority, t.Category, t.CreatedByName,
        t.CreatedByPlatformUserId is not null, t.CreatedAt, t.UpdatedAt, t.ResolvedAt);

    public static TenantSupportTicketCommentDto ToTenantDto(SupportTicketComment c) => new(
        c.Id, c.TicketId, c.AuthorName, c.AuthorPlatformUserId is not null, c.Body, c.CreatedAt);

    public static TenantSupportTicketAttachmentDto ToTenantDto(SupportTicketAttachment a) => new(
        a.Id, a.TicketId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedByName,
        a.UploadedByPlatformUserId is not null, a.CreatedAt);

    /// <summary>Internal notes and internal files never leave the platform side.</summary>
    public static TenantSupportTicketDetailDto ToTenantDetailDto(SupportTicket t) => new(
        ToTenantDto(t),
        t.Comments.Where(c => !c.IsInternal).OrderBy(c => c.CreatedAt).Select(ToTenantDto).ToList(),
        t.Attachments.Where(a => !a.IsInternal).OrderBy(a => a.CreatedAt).Select(ToTenantDto).ToList());

    public static PlatformAnnouncementDto ToDto(PlatformAnnouncement a) => new(
        a.Id, a.Title, a.Body, a.Severity, a.Audience, a.IsPublished, a.PublishedAt, a.CreatedAt, a.UpdatedAt);

    public static ActiveAnnouncementDto ToActiveDto(PlatformAnnouncement a) => new(
        a.Id, a.Title, a.Body, a.Severity, a.PublishedAt);
}
