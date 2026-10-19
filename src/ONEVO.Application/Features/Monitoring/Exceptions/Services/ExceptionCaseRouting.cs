using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Contracts;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

/// <summary>
/// Which exception cases are the caller's to work - the people the escalation policies actually route a case to, so
/// nobody above sees a case just because their coverage or HR permission is wide: the subject's attendance approver
/// (attendance:approve, ResolveApprovalInboxScopeAsync), whoever the latest escalation went to (scope.HeldCaseIds), and -
/// for monitoring cases only (ReportingLineThenHrPolicy) - HR for the cases of employees nobody can review, or escalated
/// before the escalation journal existed. Calendar cases never reach HR (ReportingLineThenLogPolicy). Shared by the
/// Inbox, the My Team card and the employee overview so they can never disagree.
/// </summary>
public sealed class ExceptionCaseRouting
{
    private const string MonitoringSource = "monitoring";
    private const string MonitoringCaseKind = "exception_case";

    private readonly IEmployeeRepository _employees;
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly IInboxDecisionJournal _journal;

    public ExceptionCaseRouting(IEmployeeRepository employees, IEmployeeAuthorityResolver authority, IInboxDecisionJournal journal)
    {
        _employees = employees;
        _authority = authority;
        _journal = journal;
    }

    /// <summary>The ids of <paramref name="cases"/> routed to the caller. <paramref name="hrFallback"/>: monitoring cases
    /// fall back to HR, calendar cases never do.</summary>
    public async Task<HashSet<Guid>> RoutedCasesAsync(
        Guid tenantId, IReadOnlyList<MonitoringException> cases, ExceptionScope scope, bool hrFallback, CancellationToken ct)
    {
        var routed = new HashSet<Guid>();
        var candidates = cases.Where(c => c.EmployeeId != scope.ActorEmployeeId).ToList();
        if (candidates.Count == 0)
            return routed;

        // Whoever the latest escalation went to holds the case now.
        foreach (var c in candidates)
            if (scope.HeldCaseIds?.Contains(c.Id) == true)
                routed.Add(c.Id);

        // The subject's own attendance approver (one batched check per legal entity).
        var people = await _employees.ListByIdsAsync(tenantId, candidates.Select(c => c.EmployeeId).Distinct().ToList(), ct);
        var approverOf = await ApproverOfAsync(candidates.Select(c => c.EmployeeId), people, ct);
        foreach (var c in candidates)
            if (approverOf.Contains(c.EmployeeId))
                routed.Add(c.Id);

        if (!hrFallback || !scope.IsHr || candidates.All(c => routed.Contains(c.Id)))
            return routed;

        // HR: an escalated case with no journal entry (escalated before the journal existed, so it went to HR), and the
        // cases of employees with no attendance approver at all (ReportingLineThenHrPolicy.InitialAsync falls back to HR).
        var escalated = (await _journal.ListLatestEscalationsAsync(tenantId, MonitoringSource, MonitoringCaseKind, ct))
            .Select(e => e.ItemId).ToHashSet();
        var noApprover = new Dictionary<Guid, bool>();
        foreach (var c in candidates.Where(c => !routed.Contains(c.Id)))
        {
            if (c.Status == ExceptionStatus.Escalated && !escalated.Contains(c.Id))
            {
                routed.Add(c.Id);
                continue;
            }
            if (escalated.Contains(c.Id) || !people.ContainsKey(c.EmployeeId))
                continue;
            if (!noApprover.TryGetValue(c.EmployeeId, out var none))
                noApprover[c.EmployeeId] = none = !await HasApproverAsync(people[c.EmployeeId], ct);
            if (none)
                routed.Add(c.Id);
        }
        return routed;
    }

    /// <summary>Whether the caller reviews this employee's cases: their attendance approver, or (with
    /// <paramref name="hrFallback"/>) HR when nobody can review them. Used where one employee's cases are shown.</summary>
    public async Task<bool> ReviewsEmployeeAsync(Guid tenantId, Guid employeeId, ExceptionScope scope, bool hrFallback, CancellationToken ct)
    {
        if (employeeId == scope.ActorEmployeeId)
            return false;
        var people = await _employees.ListByIdsAsync(tenantId, [employeeId], ct);
        if (!people.TryGetValue(employeeId, out var employee))
            return false;
        if ((await ApproverOfAsync([employeeId], people, ct)).Contains(employeeId))
            return true;
        return hrFallback && scope.IsHr && !await HasApproverAsync(employee, ct);
    }

    private async Task<HashSet<Guid>> ApproverOfAsync(
        IEnumerable<Guid> employeeIds, IReadOnlyDictionary<Guid, ONEVO.Domain.Features.CoreHr.Entities.Employee> people, CancellationToken ct)
    {
        var approverOf = new HashSet<Guid>();
        foreach (var group in employeeIds.Distinct().Where(people.ContainsKey).GroupBy(id => people[id].LegalEntityId))
        {
            if (group.Key is not Guid legalEntityId)
                continue;
            approverOf.UnionWith(await _authority.ResolveApprovalInboxScopeAsync(new EmployeeApprovalInboxScopeRequest(
                legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview, group.ToList()), ct));
        }
        return approverOf;
    }

    private async Task<bool> HasApproverAsync(ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct)
        => employee.LegalEntityId is Guid legalEntityId
           && (await _authority.ResolveApproverAsync(new EmployeeApprovalRouteRequest(
               employee.Id, legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct)).IsSuccess;
}
