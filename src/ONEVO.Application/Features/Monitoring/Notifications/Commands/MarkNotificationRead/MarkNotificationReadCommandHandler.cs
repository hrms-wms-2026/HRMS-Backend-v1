using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Commands.MarkNotificationRead;

public class MarkNotificationReadCommandHandler : IRequestHandler<MarkNotificationReadCommand, Result>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;
    private readonly IDateTimeProvider _clock;

    public MarkNotificationReadCommandHandler(
        INotificationRepository notifications,
        ICurrentUser currentUser,
        IEmployeeRepository employees,
        IDateTimeProvider clock)
    {
        _notifications = notifications;
        _currentUser = currentUser;
        _employees = employees;
        _clock = clock;
    }

    public async Task<Result> Handle(MarkNotificationReadCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result.Forbidden("Authentication required.");

        // Notification.EmployeeId is written by LocationRuleEvaluatorJob/WellnessRuleEvaluatorJob using
        // the real CoreHR Employee.Id (see ITrayEmployeeIdentityResolver's doc comment for the
        // analogous tray-side rule) - must resolve the caller's own Employee.Id here too instead of
        // comparing the raw session UserId. Uses GetDefaultForUserAsync directly rather than
        // ICallerIdentityResolver (WorkManagement-scoped, and backed by a different
        // IEmployeeRepository.GetByUserIdAsync with different semantics) to stay consistent with the
        // sibling web-side Monitoring handlers that read this same job-written data
        // (GetMyFocusStatus/GetMyActivityTimeline/GetMyWorkPattern), and to avoid a cross-bounded-context
        // dependency for what is otherwise a two-call-site need. Deliberately does not branch on
        // _currentUser.LegalEntityId the way the tray resolver does - the siblings don't either, and a
        // pre-onboarding fallback to the raw UserId (as the tray resolver logs a warning and does) is
        // out of scope here: with no Employee row, no notification can legitimately belong to this
        // caller, so NotFound is correct.
        var employee = await _employees.GetDefaultForUserAsync(_currentUser.TenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result.NotFound("Notification not found.");

        var notification = await _notifications.GetByIdAsync(_currentUser.TenantId, request.NotificationId, ct);
        if (notification is null || notification.EmployeeId != employee.Id)
            return Result.NotFound("Notification not found.");

        notification.ReadAt = _clock.UtcNow;
        _notifications.Update(notification);
        await _notifications.SaveChangesAsync(ct);

        return Result.Success();
    }
}
