using System.Net;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Domain.Features.Conversations.Entities;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>The session's tenant, EmployeeId and UserId for conversation handlers.</summary>
public sealed record ConversationCallerInfo(Guid TenantId, Guid EmployeeId, Guid UserId);

public static class ConversationCaller
{
    /// <summary>Null when not signed in or the user has no employee record in this tenant.</summary>
    public static async Task<ConversationCallerInfo?> ResolveAsync(ICurrentUser currentUser, ICallerIdentityResolver identity, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return null;
        var employeeId = await identity.ResolveCallerEmployeeIdAsync(currentUser.TenantId, currentUser.UserId, ct);
        return employeeId is null ? null : new ConversationCallerInfo(currentUser.TenantId, employeeId.Value, currentUser.UserId);
    }
}

public static class ConversationRealtime
{
    /// <summary>After someone was removed or left: everyone (including them) gets participantRemoved, the others get
    /// the system line, and the person who went gets inboxChanged (the item leaves their inbox).</summary>
    public static async Task AfterDepartureAsync(
        Contracts.IInboxRealtimePublisher realtime, Microsoft.Extensions.Logging.ILogger logger, Guid tenantId,
        ConversationContext context, Guid departedEmployeeId, ConversationMessage systemMessage, CancellationToken ct)
    {
        IReadOnlyCollection<Guid> everyone = context.ParticipantIds.Append(departedEmployeeId).Distinct().ToList();
        var remaining = everyone.Where(id => id != departedEmployeeId).ToList();
        await ConversationRules.PublishSafelyAsync(
            () => realtime.ParticipantRemovedAsync(tenantId, everyone, context.Conversation.Id, departedEmployeeId, ct), logger, "participantRemoved");
        await ConversationRules.PublishSafelyAsync(
            () => realtime.MessageCreatedAsync(tenantId, remaining, context.Conversation.Id,
                ConversationResponseBuilder.Message(systemMessage, [], author: null), ct), logger, "messageCreated");
        await ConversationRules.PublishSafelyAsync(
            () => realtime.InboxChangedAsync(tenantId, [departedEmployeeId], context.Subject, ct), logger, "inboxChanged");
    }
}

public static class ConversationSystemMessages
{
    /// <summary>A system line such as "Anu removed Priya"; <paramref name="text"/> is plain text, HTML-encoded here.</summary>
    public static ConversationMessage Create(
        Guid tenantId, Guid conversationId, string text, string systemEventJson, DateTimeOffset now, Guid createdByUserId)
        => new()
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = conversationId, AuthorEmployeeId = null,
            Kind = ConversationMessageKinds.System, BodyHtml = WebUtility.HtmlEncode(text), SystemEventJson = systemEventJson,
            CreatedById = createdByUserId, CreatedAt = now
        };
}
