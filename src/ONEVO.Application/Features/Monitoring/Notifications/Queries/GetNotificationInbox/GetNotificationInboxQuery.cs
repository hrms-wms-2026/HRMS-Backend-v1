using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Notifications.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Notifications.Queries.GetNotificationInbox;

public record GetNotificationInboxQuery : IRequest<Result<PagedResult<NotificationInboxItemDto>>>
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
