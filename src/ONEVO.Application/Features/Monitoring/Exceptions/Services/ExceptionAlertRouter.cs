using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.Models;
using ONEVO.Application.Features.CoreHr.EmployeeAuthority.ServiceInterfaces;
using ONEVO.Application.Features.Monitoring.Exceptions.ServiceInterfaces;
using MonitoringException = ONEVO.Domain.Features.Monitoring.Exceptions.Entities.Exception;

namespace ONEVO.Application.Features.Monitoring.Exceptions.Services;

public sealed class ExceptionAlertRouter : IExceptionAlertRouter
{
    private readonly IEmployeeAuthorityResolver _authority;
    private readonly IEmployeeRepository _employees;
    private readonly IPermissionRepository _permissions;
    private readonly INotificationDispatcher _notifications;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<ExceptionAlertRouter> _logger;

    public ExceptionAlertRouter(
        IEmployeeAuthorityResolver authority,
        IEmployeeRepository employees,
        IPermissionRepository permissions,
        INotificationDispatcher notifications,
        IDateTimeProvider clock,
        ILogger<ExceptionAlertRouter>? logger = null)
    {
        _authority = authority;
        _employees = employees;
        _permissions = permissions;
        _notifications = notifications;
        _clock = clock;
        _logger = logger ?? NullLogger<ExceptionAlertRouter>.Instance;
    }

    public async Task NotifyDetectedAsync(MonitoringException exception, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(exception.TenantId, exception.EmployeeId, ct);
        var recipients = await ResolveManagerAsync(exception, employee, ct);
        if (recipients.Count == 0)
            recipients = await ResolveHrAsync(exception, employee, ct);

        await SendAsync(exception, employee, ExceptionPermissions.DetectedTemplate, recipients, ct);
    }

    public async Task NotifyEscalatedAsync(MonitoringException exception, CancellationToken ct)
    {
        var employee = await _employees.GetByIdAsync(exception.TenantId, exception.EmployeeId, ct);
        var recipients = await ResolveHrAsync(exception, employee, ct);
        await SendAsync(exception, employee, ExceptionPermissions.EscalatedTemplate, recipients, ct);
    }

    /// <summary>The employee's reporting manager, through the same authority resolver the other
    /// approval flows use. Never the employee themselves.</summary>
    private async Task<IReadOnlyList<Guid>> ResolveManagerAsync(
        MonitoringException exception, ONEVO.Domain.Features.CoreHr.Entities.Employee? employee, CancellationToken ct)
    {
        if (employee?.LegalEntityId is not Guid legalEntityId)
            return [];

        var route = await _authority.ResolveApproverAsync(
            new EmployeeApprovalRouteRequest(
                employee.Id, legalEntityId, ExceptionPermissions.ManagerReview,
                EmployeeAuthorityPurpose.ExceptionAlertReview), ct);

        return route.IsSuccess && route.Value is not null && route.Value.ApproverUserId != employee.UserId
            ? [route.Value.ApproverUserId]
            : [];
    }

    private async Task<IReadOnlyList<Guid>> ResolveHrAsync(
        MonitoringException exception, ONEVO.Domain.Features.CoreHr.Entities.Employee? employee, CancellationToken ct)
    {
        var hr = await _permissions.ListUserIdsWithPermissionCodeAsync(
            exception.TenantId, ExceptionPermissions.HrReview, _clock.UtcNow, ct);
        return hr.Where(id => id != employee?.UserId).Distinct().ToList();
    }

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
