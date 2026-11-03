using Microsoft.AspNetCore.SignalR;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Conversations.DTOs;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Api.Hubs;

/// <summary>Sends inbox events to the recipients' InboxHub user groups. Recipients are employees; the hub
/// groups are keyed by UserId, so each goes through the same active-employee lookup the bell uses.
/// Failures are logged and swallowed - realtime never fails the request that triggered it.</summary>
public sealed class SignalRInboxRealtimePublisher(
    IHubContext<InboxHub> hub,
    IMilestoneMembershipCoordinator membership,
    ILogger<SignalRInboxRealtimePublisher> logger) : IInboxRealtimePublisher
{
    public Task MessageCreatedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationMessageResponse message,
        CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.MessageCreated, new { conversationId, message }, ct);

    public Task ParticipantAddedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationParticipantResponse participant,
        CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.ParticipantAdded, new { conversationId, participant }, ct);

    public Task InboxChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, ItemRef item, CancellationToken ct = default)
        => SendAsync(tenantId, employeeIds, InboxHub.InboxChanged,
            new { item = new { source = item.Source, kind = item.Kind, id = item.Id } }, ct);

    public Task MessageUpdatedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, ConversationMessageResponse message,
        CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.MessageUpdated, new { conversationId, message }, ct);

    public Task MessageDeletedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.MessageDeleted, new { conversationId, messageId }, ct);

    public Task ParticipantReadAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, Guid employeeId,
        DateTimeOffset readAt, CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.ParticipantRead, new { conversationId, employeeId, readAt }, ct);

    public Task MessageHiddenAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.MessageHidden, new { conversationId, messageId }, ct);

    public Task ParticipantRemovedAsync(
        Guid tenantId, IReadOnlyCollection<Guid> recipientEmployeeIds, Guid conversationId, Guid employeeId, CancellationToken ct = default)
        => SendAsync(tenantId, recipientEmployeeIds, InboxHub.ParticipantRemoved, new { conversationId, employeeId }, ct);

    private async Task SendAsync(Guid tenantId, IReadOnlyCollection<Guid> employeeIds, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            var groups = new List<string>();
            foreach (var employeeId in employeeIds.Where(id => id != Guid.Empty).Distinct())
            {
                var employee = await membership.GetActiveAssigneeAsync(tenantId, employeeId, ct);
                if (employee is not null && employee.UserId != Guid.Empty)
                    groups.Add(InboxHub.UserGroup(tenantId, employee.UserId));
            }
            if (groups.Count == 0)
                return;

            await hub.Clients.Groups(groups).SendAsync(eventName, payload, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Inbox realtime {Event} failed for tenant {TenantId}", eventName, tenantId);
        }
    }
}
