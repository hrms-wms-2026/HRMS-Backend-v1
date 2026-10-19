using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Dashboard.Team.Abstractions;
using ONEVO.Application.Features.Dashboard.Team.DTOs;
using ONEVO.Application.Features.Monitoring.Exceptions.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;
using ONEVO.Domain.Features.Monitoring.Exceptions.Entities;

namespace ONEVO.Application.Features.Dashboard.Team.Sources;

/// <summary>Approvals &amp; Exceptions source: monitoring exceptions the caller can act on (My
/// Team spec §8.2). Reuses the exact gate GetExceptionsQueryHandler already calls -
/// IExceptionRepository.ListEmployeeIdsWithExceptionsAsync (candidates) then
/// IExceptionScopeResolver.ResolveAsync(forAction:false, candidates) - so visibility can never
/// drift from the exceptions list screen. Actionable = Open union Escalated (counted and listed
/// together); Acknowledged is reported separately as inProgressCount, never mixed into
/// pendingCount. Ordered by EscalatedAt ?? DetectedAt, oldest first. Deliberately NOT filtered by
/// legal entity: an HR-unrestricted scope (IsHr) spans the whole tenant by design (spec §8.2
/// "Rules" / AC-11) - the card subtitle communicates this, not a predicate change here. Like the Inbox, only the cases
/// routed to the caller count (<see cref="ExceptionCaseRouting"/>) - wide coverage or HR alone shows nothing.</summary>
public sealed class MonitoringExceptionTeamActionSource(
    ICurrentUser currentUser,
    IExceptionScopeResolver scopeResolver,
    IExceptionRepository exceptions,
    IEmployeeRepository employees,
    ExceptionCaseRouting? routing = null)
    : ITeamActionSource
{
    private static readonly IReadOnlyCollection<ExceptionStatus> ActionableStatuses =
        [ExceptionStatus.Open, ExceptionStatus.Escalated];

    public string Key => "monitoring.exception";
    public string Domain => ActionSourceSummary.DomainPeople;

    public async Task<bool> IsGatedAsync(CancellationToken ct = default)
    {
        var tenantId = currentUser.TenantId;
        if (tenantId == Guid.Empty)
            return false;

        var candidates = await exceptions.ListEmployeeIdsWithExceptionsAsync(tenantId, ct);
        var scope = await scopeResolver.ResolveAsync(forAction: false, candidates, ct);
        return scope is not null;
    }

    public async Task<ActionSourceSummary> GetSummaryAsync(Guid legalEntityId, int top, CancellationToken ct = default)
    {
        var tenantId = currentUser.TenantId;
        var candidates = await exceptions.ListEmployeeIdsWithExceptionsAsync(tenantId, ct);
        var scope = await scopeResolver.ResolveAsync(forAction: false, candidates, ct);
        if (scope is null)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, 0, null, []);

        var scopedEmployeeIds = scope.IsHr ? null : scope.EmployeeIds;

        var actionableFilter = new ExceptionListFilter(
            Status: null, Type: null, scopedEmployeeIds, scope.ActorEmployeeId, ActionableStatuses, Calendar: false);
        var inProgressFilter = new ExceptionListFilter(
            ExceptionStatus.Acknowledged, Type: null, scopedEmployeeIds, scope.ActorEmployeeId, Calendar: false);

        var actionableRows = await exceptions.GetListAsync(tenantId, actionableFilter, 1, int.MaxValue, ct);
        int inProgressCount;
        if (routing is null)
            inProgressCount = await exceptions.GetListTotalCountAsync(tenantId, inProgressFilter, ct);
        else
        {
            var routed = await routing.RoutedCasesAsync(tenantId, actionableRows, scope, hrFallback: true, ct);
            actionableRows = actionableRows.Where(e => routed.Contains(e.Id)).ToList();
            var inProgressRows = await exceptions.GetListAsync(tenantId, inProgressFilter, 1, int.MaxValue, ct);
            var routedInProgress = await routing.RoutedCasesAsync(tenantId, inProgressRows, scope, hrFallback: true, ct);
            inProgressCount = inProgressRows.Count(e => routedInProgress.Contains(e.Id));
        }

        if (actionableRows.Count == 0)
            return new ActionSourceSummary(Key, Domain, ActionSourceSummary.StatusOk, 0, inProgressCount, null, []);

        var employeeIds = actionableRows.Select(e => e.EmployeeId).Distinct().ToList();
        var employeeMap = await employees.ListByIdsAsync(tenantId, employeeIds, ct);

        var ordered = actionableRows.OrderBy(e => e.EscalatedAt ?? e.DetectedAt).ToList();
        var oldest = ordered[0].EscalatedAt ?? ordered[0].DetectedAt;

        var topItems = ordered.Take(top).Select(e =>
        {
            employeeMap.TryGetValue(e.EmployeeId, out var employee);
            var name = employee is null ? null : $"{employee.FirstName} {employee.LastName}".Trim();
            return new ActionItem(
                Key,
                e.Id,
                e.Title,
                e.EmployeeId,
                name,
                e.EscalatedAt ?? e.DetectedAt,
                e.Status.ToString(),
                new ActionItemLink(ActionItemLink.KindMonitoringException, new Dictionary<string, string>
                {
                    ["alertId"] = e.Id.ToString(),
                }));
        }).ToList();

        return new ActionSourceSummary(
            Key, Domain, ActionSourceSummary.StatusOk, ordered.Count, inProgressCount, oldest, topItems);
    }
}
