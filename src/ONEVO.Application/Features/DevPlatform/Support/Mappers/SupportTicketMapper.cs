using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Mappers;

internal static class SupportTicketMapper
{
    internal static SupportTicketDto ToDto(SupportTicket ticket) =>
        new(
            ticket.Id,
            ticket.TenantId,
            ticket.Subject,
            ticket.Description,
            ticket.Status,
            ticket.Priority,
            ticket.Category,
            ticket.CreatedByPlatformUserId,
            ticket.AssignedToPlatformUserId,
            ticket.CreatedAt,
            ticket.UpdatedAt,
            ticket.ResolvedAt);

    internal static SupportTicketCommentDto ToDto(SupportTicketComment comment) =>
        new(
            comment.Id,
            comment.TicketId,
            comment.AuthorPlatformUserId,
            comment.Body,
            comment.IsInternal,
            comment.CreatedAt);

    internal static SupportTicketListResponseDto ToListResponseDto(
        IReadOnlyList<SupportTicketDto> items, int totalCount, int page, int pageSize) =>
        new(items, totalCount, page, pageSize);
}
