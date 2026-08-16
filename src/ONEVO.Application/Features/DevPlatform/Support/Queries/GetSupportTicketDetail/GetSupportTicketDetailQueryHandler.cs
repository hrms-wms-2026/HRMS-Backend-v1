using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Support.Queries.GetSupportTicketDetail;

public sealed class GetSupportTicketDetailQueryHandler
    : IRequestHandler<GetSupportTicketDetailQuery, Result<SupportTicketDetailDto>>
{
    private readonly ISupportTicketRepository _tickets;

    public GetSupportTicketDetailQueryHandler(ISupportTicketRepository tickets)
    {
        _tickets = tickets;
    }

    public async Task<Result<SupportTicketDetailDto>> Handle(
        GetSupportTicketDetailQuery request,
        CancellationToken ct)
    {
        var ticket = await _tickets.GetByIdWithCommentsAsync(request.TicketId, ct);
        if (ticket is null)
        {
            return Result<SupportTicketDetailDto>.NotFound("Support ticket not found.");
        }

        var comments = ticket.Comments
            .OrderBy(c => c.CreatedAt)
            .Select(SupportTicketMapper.ToDto)
            .ToList();

        return Result<SupportTicketDetailDto>.Success(
            new SupportTicketDetailDto(SupportTicketMapper.ToDto(ticket), comments));
    }
}
