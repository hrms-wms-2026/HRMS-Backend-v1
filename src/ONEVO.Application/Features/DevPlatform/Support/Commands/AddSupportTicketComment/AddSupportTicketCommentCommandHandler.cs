using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.AddSupportTicketComment;

public sealed class AddSupportTicketCommentCommandHandler
    : IRequestHandler<AddSupportTicketCommentCommand, Result<SupportTicketCommentDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public AddSupportTicketCommentCommandHandler(
        ISupportTicketRepository tickets,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketCommentDto>> Handle(
        AddSupportTicketCommentCommand request,
        CancellationToken ct)
    {
        var body = request.Body.Trim();
        if (string.IsNullOrWhiteSpace(body) || body.Length > 4000)
        {
            return Result<SupportTicketCommentDto>.Failure("body is required and must be at most 4000 characters.");
        }

        var ticket = await _tickets.GetByIdAsync(request.TicketId, ct);
        if (ticket is null)
        {
            return Result<SupportTicketCommentDto>.NotFound("Support ticket not found.");
        }

        var comment = new SupportTicketComment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorPlatformUserId = request.AuthorPlatformUserId,
            Body = body,
            IsInternal = request.IsInternal,
            CreatedAt = _clock.UtcNow,
        };

        await _tickets.AddCommentAsync(comment, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SupportTicketCommentDto>.Success(SupportTicketMapper.ToDto(comment));
    }
}
