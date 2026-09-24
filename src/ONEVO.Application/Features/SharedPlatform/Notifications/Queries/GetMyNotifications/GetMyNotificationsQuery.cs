using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.DTOs.Responses;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Queries.GetMyNotifications;

public sealed record GetMyNotificationsQuery(bool UnreadOnly = false, int Page = 1, int PageSize = 20)
    : IRequest<Result<IReadOnlyList<NotificationResponse>>>;
