using System.Text.Json;
using ONEVO.Application.Features.Calendar.Inbox;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// A reviewer working on an exception case (monitoring or calendar) in its chat - posting a message, adding or removing
/// someone, reacting - has seen it, so an Open case becomes Acknowledged and the "not handled in time" escalation stops.
/// It runs the normal Acknowledge action: the same reviewer scope check (the subject employee and anyone who cannot act
/// on the case are skipped silently), the same journal row, so the reviewer can undo it within the usual window exactly
/// like a manual Acknowledge. Undoing only reopens the case - the chat is untouched. Manual escalation is not chat
/// activity and never passes through here.
/// </summary>
public sealed class ExceptionCaseActivityAcknowledger : IConversationActivityListener
{
    private readonly IExceptionRepository _exceptions;
    private readonly ExceptionCaseActions _actions;
    private readonly IInboxRealtimePublisher _realtime;

    public ExceptionCaseActivityAcknowledger(IExceptionRepository exceptions, ExceptionCaseActions actions, IInboxRealtimePublisher realtime)
    {
        _exceptions = exceptions;
        _actions = actions;
        _realtime = realtime;
    }

    public static string NoteFor(string activity) => activity switch
    {
        ConversationActivities.MessagePosted => "Acknowledged automatically: replied in the chat.",
        ConversationActivities.MemberAdded => "Acknowledged automatically: added someone to the chat.",
        ConversationActivities.MemberRemoved => "Acknowledged automatically: removed someone from the chat.",
        ConversationActivities.Reacted => "Acknowledged automatically: reacted in the chat.",
        _ => "Acknowledged automatically.",
    };

    public async Task OnActivityAsync(Guid tenantId, ItemRef subject, Guid actorEmployeeId, string activity, CancellationToken ct)
    {
        var isCase = (subject.Source == ExceptionInboxKinds.Source && subject.Kind == ExceptionInboxKinds.Case)
            || (subject.Source == CalendarInboxKinds.Source && subject.Kind == CalendarInboxKinds.MeetingException);
        if (!isCase)
            return;
        var exception = await _exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is not { Status: ExceptionStatus.Open } || exception.EmployeeId == actorEmployeeId)
            return;

        var body = JsonSerializer.SerializeToElement(new { comment = NoteFor(activity) });
        var result = await _actions.RunAsync(tenantId, subject, ExceptionCaseInboxActions.Acknowledge, body, actorEmployeeId, ct);
        if (result.IsSuccess && result.Value!.AffectedEmployeeIds.Count > 0)
            await _realtime.InboxChangedAsync(tenantId, result.Value.AffectedEmployeeIds, subject, ct);
    }
}
