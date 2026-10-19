using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Conversations.DTOs;

namespace ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;

/// <summary>Messages created before <paramref name="Before"/> (the newest page when null), returned oldest first.</summary>
public sealed record ListConversationMessagesQuery(Guid ConversationId, DateTimeOffset? Before, int Take = 50)
    : IRequest<Result<ConversationMessagesPageResponse>>;
