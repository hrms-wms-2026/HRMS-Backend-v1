using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Support.DTOs.Responses;

namespace ONEVO.Application.Features.DevPlatform.Support.Commands.AddSupportTicketComment;

public sealed record AddSupportTicketCommentCommand(
    Guid TicketId,
    string Body,
    bool IsInternal,
    Guid? AuthorPlatformUserId) : IRequest<Result<SupportTicketCommentDto>>;
