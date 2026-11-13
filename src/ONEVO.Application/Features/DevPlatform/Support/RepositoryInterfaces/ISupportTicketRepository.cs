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
    int PageSize,
    bool AwaitingSupportOnly = false);

/// <summary>A ticket plus the display names a list or detail screen needs.</summary>
public sealed record SupportTicketRow(SupportTicket Ticket, string TenantName, string? AssignedToName);

public sealed record SupportAssigneeRow(Guid Id, string FullName, string Email);

public sealed record SupportCounts(int AwaitingSupport, int AwaitingSupportAssignedTo, int OpenUnassigned);

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

    /// <summary>Counts across every tenant for the admin console bell. <paramref name="platformUserId"/> scopes "assigned to".</summary>
    Task<SupportCounts> CountAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Tenant users who wrote a comment on the ticket (not platform staff).</summary>
    Task<IReadOnlyList<Guid>> ListTenantCommenterIdsAsync(Guid ticketId, CancellationToken ct = default);

    Task<string?> GetTenantNameAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Full name of an active platform user, or null when missing or not active.</summary>
    Task<string?> GetActivePlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Email of an active platform user, or null when missing or not active.</summary>
    Task<string?> GetActivePlatformUserEmailAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Display name of a platform user regardless of status (for labels on existing rows).</summary>
    Task<string?> GetPlatformUserNameAsync(Guid platformUserId, CancellationToken ct = default);

    Task<string?> GetTenantUserNameAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<SupportAssigneeRow>> ListActivePlatformUsersAsync(CancellationToken ct = default);

    Task AddAsync(SupportTicket ticket, CancellationToken ct = default);

    Task AddCommentAsync(SupportTicketComment comment, CancellationToken ct = default);

    Task AddAttachmentAsync(SupportTicketAttachment attachment, CancellationToken ct = default);
}
