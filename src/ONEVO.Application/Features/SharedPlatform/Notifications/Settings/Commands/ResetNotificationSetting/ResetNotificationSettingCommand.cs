using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;

/// <summary>Removes the current tenant's override so the event uses product defaults again.</summary>
public sealed record ResetNotificationSettingCommand(string EventKey) : IRequest<Result<NotificationSettingResponse>>;
