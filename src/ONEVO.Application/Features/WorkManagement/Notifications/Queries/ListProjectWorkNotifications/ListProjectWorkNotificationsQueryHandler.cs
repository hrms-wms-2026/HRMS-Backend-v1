using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.WorkManagement.Common.Services;
using ONEVO.Application.Features.WorkManagement.Notifications.DTOs;
using ONEVO.Application.Features.WorkManagement.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.WorkManagement.Notifications.Services;
using ONEVO.Application.Features.WorkManagement.Tasks.Services;
using ONEVO.Domain.Features.WorkManagement.Approvals.Entities;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

public sealed class ListProjectWorkNotificationsQueryHandler
    : IRequestHandler<ListProjectWorkNotificationsQuery, Result<IReadOnlyList<WorkNotificationLogResponse>>>
{
    public const int PageSize = 50;

    private readonly ICurrentUser _currentUser;
    private readonly ICallerIdentityResolver _identity;
    private readonly IWorkNotificationLogRepository _logs;
    private readonly ITaskAccessResolver? _taskAccess;

    public ListProjectWorkNotificationsQueryHandler(
        ICurrentUser currentUser, ICallerIdentityResolver identity, IWorkNotificationLogRepository logs,
        ITaskAccessResolver? taskAccess = null)
    {
        _currentUser = currentUser;
        _identity = identity;
        _logs = logs;
        _taskAccess = taskAccess;
    }

    public async Task<Result<IReadOnlyList<WorkNotificationLogResponse>>> Handle(ListProjectWorkNotificationsQuery query, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<WorkNotificationLogResponse>>.Forbidden("Authentication required.");

        var tenantId = _currentUser.TenantId;
        var callerEmployeeId = await _identity.ResolveCallerEmployeeIdAsync(tenantId, _currentUser.UserId, ct);
        if (callerEmployeeId is null)
            return Result<IReadOnlyList<WorkNotificationLogResponse>>.Forbidden("No employee record for the current user.");

        var page = Math.Max(1, query.Page);
        var rows = await _logs.ListForRecipientAsync(tenantId, query.ProjectId, callerEmployeeId.Value, (page - 1) * PageSize, PageSize, ct);
        if (_taskAccess is not null)
        {
            var visibleRows = new List<ONEVO.Domain.Features.WorkManagement.Notifications.Entities.WorkNotificationLog>(rows.Count);
            foreach (var row in rows)
            {
                if (row.TargetType != WorkTargetTypes.Task || row.TargetId is null
                    || (await _taskAccess.ResolveViewableTaskAsync(tenantId, _currentUser.UserId, row.TargetId.Value, ct)).IsSuccess)
                    visibleRows.Add(row);
            }
            rows = visibleRows;
        }
        var names = await _identity.ResolveDisplayNamesByEmployeeIdAsync(
            tenantId, rows.Select(r => r.ActorEmployeeId).Distinct().ToList(), ct);

        return Result<IReadOnlyList<WorkNotificationLogResponse>>.Success(rows.Select(r => new WorkNotificationLogResponse(
            r.Id, r.Kind, r.ActionType, WorkActionLabels.For(r.ActionType), r.TargetType, r.TargetId, r.TargetTitle,
            r.ActorEmployeeId, names.GetValueOrDefault(r.ActorEmployeeId) ?? "A teammate", r.ApprovalRequestId, r.CreatedAt)).ToList());
    }
}
