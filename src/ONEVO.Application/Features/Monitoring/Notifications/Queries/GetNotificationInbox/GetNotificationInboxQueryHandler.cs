using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.CoreHr.Employee.RepositoryInterfaces;
using ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;
using ONEVO.Application.Features.Monitoring.Notifications.RepositoryInterfaces;

namespace ONEVO.Application.Features.Monitoring.Notifications.Queries.GetNotificationInbox;

public class GetNotificationInboxQueryHandler
    : IRequestHandler<GetNotificationInboxQuery, Result<PagedResult<NotificationInboxItemDto>>>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;
    private readonly IEmployeeRepository _employees;

    public GetNotificationInboxQueryHandler(
        INotificationRepository notifications, ICurrentUser currentUser, IEmployeeRepository employees)
    {
        _notifications = notifications;
        _currentUser = currentUser;
        _employees = employees;
    }

    public async Task<Result<PagedResult<NotificationInboxItemDto>>> Handle(
        GetNotificationInboxQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.TenantId == Guid.Empty)
            return Result<PagedResult<NotificationInboxItemDto>>.Forbidden("Authentication required.");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        var tenantId = _currentUser.TenantId;

        // GetInboxAsync/GetInboxTotalCountAsync filter on Notification.EmployeeId, which the
        // alert-evaluation jobs write as the real CoreHR Employee.Id - must resolve it here too
        // instead of passing the raw session UserId (see MarkNotificationReadCommandHandler for the
        // full rationale; same resolution approach).
        var employee = await _employees.GetDefaultForUserAsync(tenantId, _currentUser.UserId, ct);
        if (employee is null)
            return Result<PagedResult<NotificationInboxItemDto>>.Success(
                new PagedResult<NotificationInboxItemDto>([], page, pageSize, 0));

        var employeeId = employee.Id;

        var total = await _notifications.GetInboxTotalCountAsync(tenantId, employeeId, ct);
        var items = await _notifications.GetInboxAsync(tenantId, employeeId, page, pageSize, ct);

        var dtos = items.Select(n => new NotificationInboxItemDto(
            n.Id, n.Type.ToString(), n.Title, n.Message, n.CreatedAt, n.ReadAt)).ToList();

        return Result<PagedResult<NotificationInboxItemDto>>.Success(
            new PagedResult<NotificationInboxItemDto>(dtos, page, pageSize, total));
    }
}
