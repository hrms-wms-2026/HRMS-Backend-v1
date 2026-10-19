namespace ONEVO.Application.Features.Conversations.RepositoryInterfaces;

/// <summary>Active employees of the tenant, for adding people to a conversation. Unlike the People
/// directory it is not limited by the caller's permissions or reporting line: any participant may add
/// any active colleague (D5), and only names and avatars are exposed.</summary>
public interface IConversationPeopleDirectory
{
    /// <summary>Ids of active employees whose name contains <paramref name="search"/> (all when empty), by name.</summary>
    Task<IReadOnlyList<Guid>> SearchActiveAsync(Guid tenantId, string? search, int take, CancellationToken ct = default);
}
