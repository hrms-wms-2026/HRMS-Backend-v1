using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Storage.EntityAssets.ServiceInterfaces;

namespace ONEVO.Application.Features.Inbox.Chats;

/// <summary>A group chat's photo is shown in the chat list of its members - only they may read it.</summary>
public sealed class ConversationEntityAssetAccessPolicy : IEntityAssetAccessPolicy
{
    private readonly InboxCallerResolver _callers;
    private readonly IConversationAccess _access;

    public ConversationEntityAssetAccessPolicy(InboxCallerResolver callers, IConversationAccess access)
    {
        _callers = callers;
        _access = access;
    }

    public async Task<bool> CanReadAsync(Guid tenantId, Guid ownerId, CancellationToken ct = default)
    {
        if (await _callers.ResolveAsync(ct) is not { } caller || caller.TenantId != tenantId)
            return false;
        return await _access.LoadAsync(tenantId, ownerId, caller.EmployeeId, ct) is not null;
    }
}
