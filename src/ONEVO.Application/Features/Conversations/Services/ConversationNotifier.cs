using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.WorkManagement.Common.OutboxHandlers;
using ONEVO.Application.Features.WorkManagement.Objectives.Services;

namespace ONEVO.Application.Features.Conversations.Services;

/// <summary>Bell notifications for conversation events. Conversations speak EmployeeId; the bell is keyed
/// by UserId, so recipients go through the same active-employee lookup and outbox path WorkNotificationEngine
/// uses. NotificationResponse carries no placeholders, so the item's source and kind travel in
/// RelatedEntityType as "inbox:{source}:{kind}" with the subject id as RelatedEntityId.</summary>
public sealed class ConversationNotifier
{
    public const string MentionedTemplate = "conversation_mentioned";
    public const string MemberAddedTemplate = "conversation_member_added";
    public const string RelatedEntityTypePrefix = "inbox:";

    private readonly IMilestoneMembershipCoordinator _membership;
    private readonly IOutboxWriter _outbox;

    public ConversationNotifier(IMilestoneMembershipCoordinator membership, IOutboxWriter outbox)
    {
        _membership = membership;
        _outbox = outbox;
    }

    public static string RelatedEntityType(ItemRef subject) => $"{RelatedEntityTypePrefix}{subject.Source}:{subject.Kind}";

    /// <summary>Enqueues one notification per active recipient, inside the caller's transaction.</summary>
    public async Task NotifyAsync(
        Guid tenantId, string templateCode, IEnumerable<Guid> recipientEmployeeIds, string actorName, string itemTitle,
        ItemRef subject, CancellationToken ct)
    {
        foreach (var employeeId in recipientEmployeeIds.Distinct())
        {
            var employee = await _membership.GetActiveAssigneeAsync(tenantId, employeeId, ct);
            if (employee is null)
                continue;

            var placeholders = new Dictionary<string, string> { ["actorName"] = actorName, ["itemTitle"] = itemTitle };
            await _outbox.EnqueueAsync(
                OutboxMessageTypes.WorkNotification,
                new WorkNotificationPayload(tenantId, employee.UserId, templateCode, placeholders, RelatedEntityType(subject), subject.Id),
                tenantId,
                ct);
        }
    }
}
