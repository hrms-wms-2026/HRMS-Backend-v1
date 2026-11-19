using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationEvents;

public sealed record ListNotificationEventsQuery(string? Module) : IRequest<Result<IReadOnlyList<NotificationEventResponse>>>;
