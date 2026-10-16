using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

public sealed class ExceptionScopeResolver : IExceptionScopeResolver
{
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly IInboxDecisionJournal _journal;

    // Calendar cases escalate up the reporting line (Calendar.Inbox.ReportingLineThenLogPolicy), so whoever they reach may sit
    // outside management coverage; monitoring cases escalate within coverage / HR and need no holder grant.
    private const string CalendarSource = "calendar";
    private const string CalendarCaseKind = "meeting_exception";

    public ExceptionScopeResolver(
        ICurrentUser currentUser, IEmployeeRepository employees, IEmployeeAuthorityResolver authority, IInboxDecisionJournal journal)
    {
        _currentUser = currentUser;
        _employees = employees;
        _authority = authority;
        _journal = journal;
    }

    public async Task<ExceptionScope?> ResolveAsync(
        bool forAction, IReadOnlyCollection<Guid> candidateEmployeeIds, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return null;

        var isHr = _currentUser.HasPermission(ExceptionPermissions.HrReview)
                   || _currentUser.HasPermission(ExceptionPermissions.HrManage);
        var isManager = _currentUser.HasPermission(ExceptionPermissions.ManagerReview);
        var codePermission = forAction ? ExceptionPermissions.Acknowledge : ExceptionPermissions.View;
        var hasCodePermission = _currentUser.HasPermission(codePermission);
        var actor = await _employees.GetDefaultForUserAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (isHr)
            return new ExceptionScope(true, actor?.Id, Array.Empty<Guid>());

        var held = actor is null ? [] : await HeldCalendarCasesAsync(actor.Id, ct);
        if (!isManager && !hasCodePermission)
            return held.Count == 0 ? null : new ExceptionScope(false, actor!.Id, Array.Empty<Guid>(), held);

        if (actor?.LegalEntityId is not Guid legalEntityId)
            return new ExceptionScope(false, actor?.Id, Array.Empty<Guid>(), held);

        var permission = isManager ? ExceptionPermissions.ManagerReview : codePermission;
        var visibility = await _authority.ResolveVisibilityAsync(
            new EmployeeAuthorityVisibilityRequest(
                _currentUser.UserId,
                legalEntityId,
                permission,
                IncludeSelf: false,
                EmployeeAuthorityPurpose.ExceptionAlertReview), ct);

        var ids = new HashSet<Guid>(visibility.EmployeeIds);

        // Visibility only expands management coverage. The alert itself is routed with
        // ResolveApproverAsync, which also falls back to the reporting line - so a reporting-line
        // manager gets notified about people coverage doesn't show them. Add every candidate the
        // caller is the exact approver for, the same rule that picked them as the recipient.
        if (isManager && candidateEmployeeIds.Count > 0)
        {
            var approverOf = await _authority.ResolveApprovalInboxScopeAsync(
                new EmployeeApprovalInboxScopeRequest(
                    legalEntityId,
                    ExceptionPermissions.ManagerReview,
                    EmployeeAuthorityPurpose.ExceptionAlertReview,
                    candidateEmployeeIds), ct);
            ids.UnionWith(approverOf);
        }

        // Company-wide / department coverage expands to every active employee, which re-introduces
        // the actor - strip them so a manager never works their own alert.
        ids.Remove(actor.Id);
        return new ExceptionScope(false, actor.Id, ids.ToList(), held);
    }

    /// <summary>The calendar cases whose latest escalation (not undone) went to this employee.</summary>
    private async Task<IReadOnlyCollection<Guid>> HeldCalendarCasesAsync(Guid actorEmployeeId, CancellationToken ct)
        => (await _journal.ListLatestEscalationsAsync(_currentUser.TenantId, CalendarSource, CalendarCaseKind, ct))
            .Where(e => InboxEscalationJournal.EscalatedTo(e).Contains(actorEmployeeId))
            .Select(e => e.ItemId)
            .ToList();
}
