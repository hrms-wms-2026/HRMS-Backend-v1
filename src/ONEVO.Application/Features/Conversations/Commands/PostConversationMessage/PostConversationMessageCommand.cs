using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;

public sealed record PostConversationMessageCommand(Guid ConversationId, string BodyHtml, IReadOnlyList<Guid>? MentionEmployeeIds)
    : IRequest<Result<ConversationMessageResponse>>;
