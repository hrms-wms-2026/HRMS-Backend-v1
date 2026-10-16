using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;

namespace ONEVO.Application.Features.Calendar.Inbox;

/// <summary>
/// Calendar meeting-exception cases (plan P6, calendar doc D8): the subject's reporting manager reviews; an
/// escalation goes to the current holder's own reporting manager; when there is nobody above, the escalation is
/// only logged - HR is never notified.
/// </summary>
public sealed class ReportingLineThenLogPolicy : IInboxEscalationPolicy
{
    public const string Source = "calendar";
    /// <summary>CalendarExceptionDefaults.ManagerSlaDays.</summary>
    public static readonly TimeSpan EscalationSla = TimeSpan.FromDays(3);
    public const string NoReviewer = "No reporting manager found - logged.";
    public const string NoOneAbove = "No one above - logged.";

    private readonly Func<Guid, IEmployeeAuthorityResolver> _authorityFor;
    private readonly IEmployeeRepository _employees;

    public ReportingLineThenLogPolicy(Func<Guid, IEmployeeAuthorityResolver> authorityFor, IEmployeeRepository employees)
    {
        _authorityFor = authorityFor;
        _employees = employees;
    }

    public string Name => "reporting_line_then_log";

    public bool AppliesTo(string source, string kindOrType) => source == Source;

    public TimeSpan? Sla => EscalationSla;

    public async Task<EscalationTarget> InitialAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
        => context.SubjectEmployeeId is { } subject
            ? await ManagerOfAsync(tenantId, subject, context, NoReviewer, ct)
            : new EscalationTarget.LogOnly(NoReviewer);

    public async Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
    {
        var holder = context.CurrentHolderEmployeeIds.FirstOrDefault();
        if (holder == Guid.Empty)
            return await InitialAsync(tenantId, context, ct);
        return await ManagerOfAsync(tenantId, holder, context, NoOneAbove, ct);
    }

    /// <summary>The employee's reporting manager - never the subject, never someone already holding the item.</summary>
    private async Task<EscalationTarget> ManagerOfAsync(
        Guid tenantId, Guid employeeId, EscalationContext context, string none, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(tenantId, employeeId, ct);
        if (employee?.LegalEntityId is not Guid legalEntityId)
            return new EscalationTarget.LogOnly(none);

        var route = await _authorityFor(tenantId).ResolveApproverAsync(
            new EmployeeApprovalRouteRequest(
                employee.Id, legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct);
        if (!route.IsSuccess || route.Value is not { } approver || approver.ApproverEmployeeId == employee.Id
            || approver.ApproverEmployeeId == context.SubjectEmployeeId || context.CurrentHolderEmployeeIds.Contains(approver.ApproverEmployeeId))
            return new EscalationTarget.LogOnly(none);
        return new EscalationTarget.Employees([new EscalationRecipient(approver.ApproverUserId, approver.ApproverEmployeeId)]);
    }
}
