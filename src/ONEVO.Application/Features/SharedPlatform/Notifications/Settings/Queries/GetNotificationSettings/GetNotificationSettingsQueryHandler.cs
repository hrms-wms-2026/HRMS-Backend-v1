using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.RepositoryInterfaces;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Mappers;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Queries.GetNotificationSettings;

public sealed class GetNotificationSettingsQueryHandler
    : IRequestHandler<GetNotificationSettingsQuery, Result<IReadOnlyList<NotificationSettingResponse>>>
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantNotificationRuleRepository _rules;
    private readonly INotificationTemplateStore _templates;

    public GetNotificationSettingsQueryHandler(
        ICurrentUser currentUser, ITenantNotificationRuleRepository rules, INotificationTemplateStore templates)
    {
        _currentUser = currentUser;
        _rules = rules;
        _templates = templates;
    }

    public async Task<Result<IReadOnlyList<NotificationSettingResponse>>> Handle(GetNotificationSettingsQuery request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<IReadOnlyList<NotificationSettingResponse>>.Forbidden("Authentication required.");

        var rules = (await _rules.ListAsync(_currentUser.TenantId, ct)).ToDictionary(r => r.EventKey, StringComparer.Ordinal);
        var templates = await _templates.ListAsync(null, ct);

        IReadOnlyList<NotificationSettingResponse> settings = NotificationEventCatalog.All
            .OrderBy(e => e.Module).ThenBy(e => e.DisplayName)
            .Select(e => NotificationSettingMapper.ToSetting(e, rules.GetValueOrDefault(e.Key), templates))
            .ToList();
        return Result<IReadOnlyList<NotificationSettingResponse>>.Success(settings);
    }
}
