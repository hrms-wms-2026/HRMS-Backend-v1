using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.WorkManagement.Common.Services;

namespace ONEVO.Application.Features.Conversations.Queries.SearchConversationPeople;

public sealed class SearchConversationPeopleQueryHandler
    : IRequestHandler<SearchConversationPeopleQuery, Result<IReadOnlyList<ConversationPersonResponse>>>
{
    public const int PageSize = 20;

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IConversationPeopleDirectory _directory;

    public SearchConversationPeopleQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IConversationPeopleDirectory directory)
    {
        _currentUser = currentUser;
        _identity = identity;
        _directory = directory;
    }

    public async Task<Result<IReadOnlyList<ConversationPersonResponse>>> Handle(SearchConversationPeopleQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<ConversationPersonResponse>>.Forbidden("Authentication required.");
        var tenantId = _currentUser.TenantId;
        var caller = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (caller is null)
            return Result<IReadOnlyList<ConversationPersonResponse>>.Forbidden(ConversationRules.NoEmployeeMessage);

        // One extra, so leaving the caller out still fills a page.
        var ids = (await _directory.SearchActiveInLegalEntityOfAsync(tenantId, caller.Value, query.Search?.Trim(), PageSize + 1, ct))
            .Where(id => id != caller.Value).Take(PageSize).ToList();
        if (ids.Count == 0)
            return Result<IReadOnlyList<ConversationPersonResponse>>.Success([]);

        var people = await _identity.ResolveIdentitiesByEmployeeIdAsync(tenantId, ids, ct);
        return Result<IReadOnlyList<ConversationPersonResponse>>.Success(ids
            .Where(people.ContainsKey)
            .Select(id => new ConversationPersonResponse(id, people[id].Name, people[id].AvatarFileId))
            .ToList());
    }
}
