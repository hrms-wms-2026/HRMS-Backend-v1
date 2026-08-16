using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.UpdateSupportTicketStatus;

public sealed record UpdateSupportTicketStatusCommand(
    Guid TicketId,
    string Status) : IRequest<Result<SupportTicketDto>>;
