using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.ListSupportTickets;

public sealed class ListSupportTicketsQueryHandler
    : IRequestHandler<ListSupportTicketsQuery, Result<SupportTicketListResponseDto>>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly ISupportTicketRepository _tickets;

    public ListSupportTicketsQueryHandler(ISupportTicketRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task<Result<SupportTicketListResponseDto>> Handle(
        ListSupportTicketsQuery request,
        CancellationToken ct)
    {
        var page = request.Page <= 0 ? 1 : request.Page;
        var size = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, MaxPageSize);
        var skip = (page - 1) * size;

        var tickets = await _tickets.ListAsync(request.Status, request.Priority, request.TenantId, skip, size, ct);
        var total = await _tickets.CountAsync(request.Status, request.Priority, request.TenantId, ct);
        var items = tickets.Select(SupportTicketMapper.ToDto).ToList();

        return Result<SupportTicketListResponseDto>.Success(
            SupportTicketMapper.ToListResponseDto(items, total, page, size));
    }
}
