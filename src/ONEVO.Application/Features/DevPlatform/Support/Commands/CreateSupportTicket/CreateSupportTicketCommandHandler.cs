using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Support.Mappers;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.CreateSupportTicket;

public sealed class CreateSupportTicketCommandHandler
    : IRequestHandler<CreateSupportTicketCommand, Result<SupportTicketDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateSupportTicketCommandHandler(
        ISupportTicketRepository tickets,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SupportTicketDto>> Handle(CreateSupportTicketCommand request, CancellationToken ct)
    {
        var subject = request.Subject.Trim();
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 200)
        {
            return Result<SupportTicketDto>.Failure("subject is required and must be at most 200 characters.");
        }

        var description = request.Description.Trim();
        if (string.IsNullOrWhiteSpace(description) || description.Length > 4000)
        {
            return Result<SupportTicketDto>.Failure("description is required and must be at most 4000 characters.");
        }

        var priority = string.IsNullOrWhiteSpace(request.Priority)
            ? SupportTicket.PriorityMedium
            : request.Priority.Trim();
        if (!SupportTicket.AllPriorities.Contains(priority))
        {
            return Result<SupportTicketDto>.Failure(
                $"priority must be one of: {string.Join(", ", SupportTicket.AllPriorities)}.");
        }

        string? category = null;
        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            category = request.Category.Trim();
            if (category.Length > 100)
            {
                return Result<SupportTicketDto>.Failure("category must be at most 100 characters.");
            }
        }

        var now = _clock.UtcNow;
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Subject = subject,
            Description = description,
            Status = SupportTicket.StatusOpen,
            Priority = priority,
            Category = category,
            CreatedByPlatformUserId = request.CreatedByPlatformUserId,
            AssignedToPlatformUserId = null,
            CreatedAt = now,
        };

        await _tickets.AddAsync(ticket, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SupportTicketDto>.Success(SupportTicketMapper.ToDto(ticket));
    }
}
