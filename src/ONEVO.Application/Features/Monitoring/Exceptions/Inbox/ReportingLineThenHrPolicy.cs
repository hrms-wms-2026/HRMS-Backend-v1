using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Monitoring.Exceptions.Services;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Inbox;

/// <summary>
/// Monitoring exception cases (the existing routing, moved here unchanged from ExceptionAlertRouter):
/// a new case goes to the subject's reporting manager (exceptions' manager review through the employee
/// authority resolver), or to HR when nobody above them can review; escalation always goes to HR.
/// The subject employee is never a reviewer of their own case.
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
        if (subject?.LegalEntityId is Guid legalEntityId)
        {
            var route = await _authorityFor(tenantId).ResolveApproverAsync(
                new EmployeeApprovalRouteRequest(
                    subject.Id, legalEntityId, ExceptionPermissions.ManagerReview, EmployeeAuthorityPurpose.ExceptionAlertReview), ct);
            if (route.IsSuccess && route.Value is { } approver && approver.ApproverUserId != subject.UserId)
                return new EscalationTarget.Employees([new EscalationRecipient(approver.ApproverUserId, approver.ApproverEmployeeId)]);
        }
        return await HrAsync(tenantId, subject, ct);
    }

    public async Task<EscalationTarget> NextAsync(Guid tenantId, EscalationContext context, CancellationToken ct)
        => await HrAsync(tenantId, await SubjectAsync(tenantId, context, ct), ct);

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
