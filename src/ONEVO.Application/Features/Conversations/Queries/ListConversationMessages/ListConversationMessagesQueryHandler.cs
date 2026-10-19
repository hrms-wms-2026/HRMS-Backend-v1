using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.Conversations.Queries.ListConversationMessages;

public sealed class ListConversationMessagesQueryHandler
    : IRequestHandler<ListConversationMessagesQuery, Result<ConversationMessagesPageResponse>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationAccess _access;
    private readonly IConversationRepository _conversations;
    private readonly ConversationResponseBuilder _responses;

    public ListConversationMessagesQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationAccess access,
        IConversationRepository conversations, ConversationResponseBuilder responses)
    {
        _currentUser = currentUser;
        _identity = identity;
        _access = access;
        _conversations = conversations;
        _responses = responses;
    }

    public async Task<Result<ConversationMessagesPageResponse>> Handle(ListConversationMessagesQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ConversationMessagesPageResponse>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<ConversationMessagesPageResponse>.Forbidden(ConversationRules.NoEmployeeMessage);

        var context = await _access.LoadAsync(tenantId, query.ConversationId, caller.Value, ct);
        if (context is null)
            return Result<ConversationMessagesPageResponse>.NotFound(ConversationRules.NotFoundMessage);

        var take = ConversationRules.ClampPageSize(query.Take);
        var page = await _conversations.ListMessagesAsync(tenantId, query.ConversationId, query.Before, take + 1, ct);
        var messages = page.Take(take).Reverse().ToList();
        return Result<ConversationMessagesPageResponse>.Success(new ConversationMessagesPageResponse(
            await _responses.MessagesAsync(tenantId, messages, ct), page.Count > take));
    }
}
