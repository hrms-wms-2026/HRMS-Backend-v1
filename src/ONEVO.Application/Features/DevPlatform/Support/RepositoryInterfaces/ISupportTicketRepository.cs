using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

public interface ISupportTicketRepository
{
    Task<SupportTicket?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<SupportTicket?> GetByIdWithCommentsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SupportTicket>> ListAsync(
        string? status,
        string? priority,
        Guid? tenantId,
        int skip,
        int take,
        CancellationToken ct = default);
    Task<int> CountAsync(
        string? status,
        string? priority,
        Guid? tenantId,
        CancellationToken ct = default);
    Task AddAsync(SupportTicket ticket, CancellationToken ct = default);
    Task AddCommentAsync(SupportTicketComment comment, CancellationToken ct = default);
}
