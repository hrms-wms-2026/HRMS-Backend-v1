using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// Monitoring exception cases: a new case goes to the subject's attendance approver (attendance:approve through the
/// employee authority resolver), or to HR when nobody can review them. An escalation goes one level up the same
/// approver chain - the attendance approver of whoever holds the case now (the subject's approver when no holder is
/// known, e.g. the SLA sweep) - and only reaches HR when there is nobody above. The subject employee is never a
/// reviewer of their own case.
/// </summary>
public sealed class ReportingLineThenHrPolicy : IInboxEscalationPolicy
{
    public const string Source = "monitoring";
    public static readonly TimeSpan EscalationSla = TimeSpan.FromDays(3);
    public const string NobodyToAlert = "No manager and no HR reviewer found.";

    private readonly Func<Guid, IEmployeeAuthorityResolver> _authorityFor;
    private readonly IEmployeeRepository _employees;
    private readonly IPermissionRepository _permissions;
    private readonly IDateTimeProvider _clock;

    /// <param name="authorityFor">The authority resolver for a tenant (request-scoped one in a request,
    /// a tenant-pinned one in a background job).</param>
    public ReportingLineThenHrPolicy(
        Func<Guid, IEmployeeAuthorityResolver> authorityFor, IEmployeeRepository employees, IPermissionRepository permissions,
        IDateTimeProvider clock)
    {
        _authorityFor = authorityFor;
        _employees = employees;
        _permissions = permissions;
        _clock = clock;
    }

    public string Name => "reporting_line_then_hr";

    /// <summary>Every monitoring exception type except the calendar meeting types (they have their own policy).</summary>
    public bool AppliesTo(string source, string kindOrType)
        => source == Source && !kindOrType.StartsWith("Meeting", StringComparison.Ordinal);

    public TimeSpan? Sla => EscalationSla;

    public async Task<EscalationTarget> InitialAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
    {
        var subject = await SubjectAsync(tenantId, context, ct);
        if (subject is not null && await ApproverOfAsync(tenantId, subject, ct) is { } approver && approver.UserId != subject.UserId)
            return new EscalationTarget.Employees([approver]);
        return await HrAsync(tenantId, subject, ct);
    }

    public async Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
    {
        var subject = await SubjectAsync(tenantId, context, ct);
        if (subject is null)
            return await HrAsync(tenantId, null, ct);

        // Who holds the case now: the given holder, else the reviewer it was first routed to.
        var holderId = context.CurrentHolderEmployeeIds.FirstOrDefault();
        if (holderId == Guid.Empty)
            holderId = (await ApproverOfAsync(tenantId, subject, ct))?.EmployeeId ?? Guid.Empty;
        if (holderId != Guid.Empty && await _employees.GetByIdAsync(tenantId, holderId, ct) is { } holder
            && await ApproverOfAsync(tenantId, holder, ct) is { } next
            && next.EmployeeId != subject.Id && next.EmployeeId != holder.Id && !context.CurrentHolderEmployeeIds.Contains(next.EmployeeId!.Value))
            return new EscalationTarget.Employees([next]);
        return await HrAsync(tenantId, subject, ct);
    }

    /// <summary>The employee's attendance approver (attendance:approve route), or null.</summary>
    private async Task<EscalationRecipient?> ApproverOfAsync(
        Guid tenantId, ONEVO.Domain.Features.CoreHr.Entities.Employee employee, CancellationToken ct)
    {
        if (employee.LegalEntityId is not Guid legalEntityId)
            return null;
        var route = await _authorityFor(tenantId).ResolveApproverAsync(
            new EmployeeApprovalRouteRequest(
                employee.Id, legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct);
        return route.IsSuccess && route.Value is { } approver
            ? new EscalationRecipient(approver.ApproverUserId, approver.ApproverEmployeeId)
            : null;
    }

    private async Task<ONEVO.Domain.Features.CoreHr.Entities.Employee?> SubjectAsync(
        Guid tenantId, EscalationContext context, CancellationToken ct)
        => context.SubjectEmployeeId is { } id ? await _employees.GetByIdAsync(tenantId, id, ct) : null;

    private async Task<EscalationTarget> HrAsync(
        Guid tenantId, ONEVO.Domain.Features.CoreHr.Entities.Employee? subject, CancellationToken ct)
    {
        var hrUserIds = (await _permissions.ListUserIdsWithPermissionCodeAsync(tenantId, ExceptionPermissions.HrReview, _clock.UtcNow, ct))
            .Where(id => id != subject?.UserId).Distinct().ToList();
        if (hrUserIds.Count == 0)
            return new EscalationTarget.LogOnly(NobodyToAlert);

        var recipients = new List<EscalationRecipient>(hrUserIds.Count);
        foreach (var userId in hrUserIds)
            recipients.Add(new EscalationRecipient(userId, (await _employees.GetDefaultForUserAsync(tenantId, userId, ct))?.Id));
        return new EscalationTarget.Employees(recipients);
    }
}
