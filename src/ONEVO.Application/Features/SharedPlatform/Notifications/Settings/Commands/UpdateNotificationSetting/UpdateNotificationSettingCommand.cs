using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;

/// <summary>Saves the current tenant's override for one event. <see cref="ExpectedRevision"/> is null when
/// the event currently uses defaults, otherwise the revision that was loaded.</summary>
public sealed record UpdateNotificationSettingCommand(
    string EventKey,
    IReadOnlyList<string> EnabledChannels,
    Guid? TemplateId,
    string? RecipientRule,
    int? ExpectedRevision)
    : IRequest<Result<NotificationSettingResponse>>;
