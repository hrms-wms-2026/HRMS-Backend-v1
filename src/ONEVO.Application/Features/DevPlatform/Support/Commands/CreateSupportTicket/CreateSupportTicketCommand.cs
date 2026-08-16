using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.CreateSupportTicket;

public sealed record CreateSupportTicketCommand(
    Guid? TenantId,
    string Subject,
    string Description,
    string? Priority,
    string? Category,
    Guid? CreatedByPlatformUserId) : IRequest<Result<SupportTicketDto>>;
