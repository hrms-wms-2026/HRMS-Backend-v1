using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Queries.GetNotificationSettings;

/// <summary>Every catalog event with the current tenant's effective delivery rule.</summary>
public sealed record GetNotificationSettingsQuery : IRequest<Result<IReadOnlyList<NotificationSettingResponse>>>;
