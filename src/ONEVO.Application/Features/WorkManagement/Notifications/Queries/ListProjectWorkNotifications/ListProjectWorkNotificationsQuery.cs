using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.WorkManagement.Notifications.DTOs;

namespace ONEVO.Application.Features.WorkManagement.Notifications.Queries.ListProjectWorkNotifications;

public sealed record ListProjectWorkNotificationsQuery(Guid ProjectId, int Page = 1)
    : IRequest<Result<IReadOnlyList<WorkNotificationLogResponse>>>;
