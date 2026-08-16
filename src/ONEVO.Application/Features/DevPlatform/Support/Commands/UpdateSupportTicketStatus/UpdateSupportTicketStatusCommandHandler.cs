using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.UpdateSupportTicketStatus;

public sealed class UpdateSupportTicketStatusCommandHandler
    : IRequestHandler<UpdateSupportTicketStatusCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateSupportTicketStatusCommandHandler(
        ISupportTicketRepository tickets,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(
        UpdateSupportTicketStatusCommand request,
        CancellationToken ct)
    {
        var status = request.Status.Trim();
        if (!SupportTicket.AllStatuses.Contains(status))
        {
            return Result<SupportTicketDto>.Failure(
                $"status must be one of: {string.Join(", ", SupportTicket.AllStatuses)}.");
        }

        var ticket = await _tickets.GetByIdAsync(request.TicketId, ct);
        if (ticket is null)
        {
            return Result<SupportTicketDto>.NotFound("Support ticket not found.");
        }

        var wasResolvedOrClosed = ticket.Status is SupportTicket.StatusResolved or SupportTicket.StatusClosed;
        var isResolvedOrClosed = status is SupportTicket.StatusResolved or SupportTicket.StatusClosed;

        if (status == SupportTicket.StatusResolved && ticket.ResolvedAt is null)
        {
            ticket.ResolvedAt = _clock.UtcNow;
        }
        else if (wasResolvedOrClosed && !isResolvedOrClosed)
        {
            ticket.ResolvedAt = null;
        }

        ticket.Status = status;
        ticket.UpdatedAt = _clock.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SupportTicketDto>.Success(SupportTicketMapper.ToDto(ticket));
    }
}
