using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Conversations.Contracts;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Inbox.Escalation;
using ONEVO.Application.Features.Inbox.Services;
using ONEVO.Application.Features.Monitoring.Exceptions.Inbox;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

public sealed class ExceptionAlertRouter : IExceptionAlertRouter
{
    private readonly ReportingLineThenHrPolicy _policy;
    private readonly IEmployeeRepository _employees;
    private readonly INotificationDispatcher _notifications;
    private readonly ILogger<ExceptionAlertRouter> _logger;
    private readonly IInboxChangeNotifier? _inbox;

    public ExceptionAlertRouter(
        IEmployeeAuthorityResolver authority,
        IEmployeeRepository employees,
        IPermissionRepository permissions,
        INotificationDispatcher notifications,
        IDateTimeProvider clock,
        ILogger<ExceptionAlertRouter>? logger = null,
        IInboxChangeNotifier? inbox = null)
    {
        // Routing (manager, else HR; escalation to HR) is the monitoring escalation policy.
        _policy = new ReportingLineThenHrPolicy(_ => authority, employees, permissions, clock);
        _employees = employees;
        _notifications = notifications;
        _logger = logger ?? NullLogger<ExceptionAlertRouter>.Instance;
        _inbox = inbox;
    }

    public async Task NotifyDetectedAsync(MonitoringException exception, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(exception.TenantId, exception.EmployeeId, ct);
        var target = await _policy.InitialAsync(exception.TenantId, Context(exception), ct);
        await SendAsync(exception, employee, ExceptionPermissions.DetectedTemplate, UserIds(target), ct);
        // The reviewers' and the subject's open inboxes refresh once the case commits (queued with the alert).
        if (_inbox is not null)
            await _inbox.EnqueueAsync(exception.TenantId, EmployeeIds(target).Append(exception.EmployeeId), InboxRefs.ExceptionCase(exception.Id), ct);
    }

    public async Task NotifyEscalatedAsync(MonitoringException exception, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(exception.TenantId, exception.EmployeeId, ct);
        var target = await _policy.NextAsync(exception.TenantId, Context(exception), ct);
        await SendAsync(exception, employee, ExceptionPermissions.EscalatedTemplate, UserIds(target), ct);
    }

    private static EscalationContext Context(MonitoringException exception)
        => new(new ItemRef(ReportingLineThenHrPolicy.Source, "exception_case", exception.Id), exception.Type.ToString(), [],
            exception.EmployeeId, null, null);

    private static IEnumerable<Guid> EmployeeIds(EscalationTarget target)
        => target is EscalationTarget.Employees employees ? employees.Recipients.Where(r => r.EmployeeId is not null).Select(r => r.EmployeeId!.Value) : [];

    private static IReadOnlyList<Guid> UserIds(EscalationTarget target)
        => target is EscalationTarget.Employees employees ? employees.Recipients.Select(r => r.UserId).ToList() : [];

    private async Task SendAsync(
        MonitoringException exception,
        ONEVO.Domain.Features.CoreHr.Entities.Employee? employee,
        string template,
        IReadOnlyList<Guid> recipients,
        CancellationToken ct)
    {
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Exception {ExceptionId} ({Type}) for employee {EmployeeId}: no manager with {ManagerPermission} and no HR user with {HrPermission} found, nobody alerted",
                exception.Id, exception.Type, exception.EmployeeId,
                ExceptionPermissions.ManagerReview, ExceptionPermissions.HrReview);
            return;
        }

        var name = employee is null ? "" : $"{employee.FirstName} {employee.LastName}".Trim();
        var placeholders = new Dictionary<string, string>
        {
            ["employeeName"] = string.IsNullOrWhiteSpace(name) ? "An employee" : name,
            ["title"] = exception.Title,
            ["description"] = exception.Description
        };

        foreach (var recipient in recipients)
        {
            await _notifications.SendTemplatedAsync(
                exception.TenantId, recipient, template, placeholders,
                ExceptionPermissions.RelatedEntityType, exception.Id, ct);
        }
    }
}
