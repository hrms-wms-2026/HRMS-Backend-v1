using ONEVO.Application.Features.Calendar.Inbox;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// A reviewer replying in an open exception case's chat has picked it up: the case is acknowledged as if they pressed
/// Acknowledge (same command, scope check and journal row, so it shows in the steps and can be undone). The employee the
/// case is about replying (their justification) never acknowledges it, and a caller outside the review scope is refused
/// by the command and the reply simply stays a message.
/// </summary>
public sealed class ExceptionCaseReplyAcknowledger : IConversationReplyHook
{
    private readonly IExceptionRepository _exceptions;
    private readonly ExceptionCaseActions _actions;

    public ExceptionCaseReplyAcknowledger(IExceptionRepository exceptions, ExceptionCaseActions actions)
    {
        _exceptions = exceptions;
        _actions = actions;
    }

    public async Task<IReadOnlyCollection<Guid>?> OnReplyAsync(Guid tenantId, ItemRef subject, Guid authorEmployeeId, CancellationToken ct)
    {
        var isCase = (subject.Source == ExceptionInboxKinds.Source && subject.Kind == ExceptionInboxKinds.Case)
            || (subject.Source == CalendarInboxKinds.Source && subject.Kind == CalendarInboxKinds.MeetingException);
        if (!isCase)
            return null;

        var exception = await _exceptions.GetByIdAsync(tenantId, subject.Id, ct);
        if (exception is null || exception.Status != ExceptionStatus.Open || exception.EmployeeId == authorEmployeeId)
            return null;

        var result = await _actions.RunAsync(tenantId, subject, ExceptionCaseInboxActions.Acknowledge, null, authorEmployeeId, ct);
        return result.IsSuccess ? result.Value!.AffectedEmployeeIds : null;
    }
}
