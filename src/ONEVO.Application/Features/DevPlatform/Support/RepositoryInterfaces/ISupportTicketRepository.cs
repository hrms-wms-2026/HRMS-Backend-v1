using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

/// <summary>Filter for ticket lists. TenantId is mandatory on every tenant-side call.</summary>
public sealed record SupportTicketFilter(
    Guid? TenantId,
    string? Status,
    string? Priority,
    Guid? AssignedToPlatformUserId,
    bool UnassignedOnly,
    string? Search,
    int Page,
    int PageSize);

/// <summary>A ticket plus the display names a list or detail screen needs.</summary>
public sealed record SupportTicketRow(SupportTicket Ticket, string TenantName, string? AssignedToName);

public sealed record SupportAssigneeRow(Guid Id, string FullName, string Email);

/// <summary>
/// Support tickets are tenant-owned (RLS Tenant predicate). Platform staff reach every tenant's rows in
/// admin context mode; a tenant request only ever sees its own tenant's rows.
/// </summary>
public interface ISupportTicketRepository
{
    Task<(IReadOnlyList<SupportTicketRow> Items, int Total)> ListAsync(SupportTicketFilter filter, CancellationToken ct = default);

    /// <summary>Tracked, with comments and attachments. Null when missing or in another tenant.</summary>
    Task<SupportTicket?> GetWithChildrenAsync(Guid ticketId, Guid? tenantId, CancellationToken ct = default);

    /// <summary>Tracked, without children. Null when missing or in another tenant.</summary>
    Task<SupportTicket?> GetAsync(Guid ticketId, Guid? tenantId, CancellationToken ct = default);

    Task<SupportTicketAttachment?> GetAttachmentAsync(
        Guid ticketId, Guid attachmentId, Guid? tenantId, CancellationToken ct = default);

    Task<int> CountAttachmentsAsync(Guid ticketId, CancellationToken ct = default);

    Task<string?> GetTenantNameAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Full name of an active platform user, or null when missing or not active.</summary>
    Task<string?> GetActivePlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Display name of a platform user regardless of status (for labels on existing rows).</summary>
    Task<string?> GetPlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default);

    Task<string?> GetTenantUserNameAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<SupportAssigneeRow>> ListActivePlatformUsersAsync(CancellationToken ct = default);

    Task AddAsync(SupportTicket ticket, CancellationToken ct = default);

    Task AddCommentAsync(SupportTicketComment comment, CancellationToken ct = default);

    Task AddAttachmentAsync(SupportTicketAttachment attachment, CancellationToken ct = default);
}
