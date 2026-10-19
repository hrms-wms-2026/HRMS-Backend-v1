using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Queries.ListNotificationTemplates;

public sealed record ListNotificationTemplatesQuery(string? EventKey, string? Search)
    : IRequest<Result<IReadOnlyList<NotificationTemplateSummaryResponse>>>;
