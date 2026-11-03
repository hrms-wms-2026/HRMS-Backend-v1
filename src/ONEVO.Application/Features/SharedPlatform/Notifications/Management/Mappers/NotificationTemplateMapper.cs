using ONEVO.Application.Features.SharedPlatform.Notifications.Catalog;
using ONEVO.Application.Features.SharedPlatform.Notifications.Helpers;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Rendering;
using ONEVO.Domain.Features.SharedPlatform.Notifications.Entities;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Management.Mappers;

public static class NotificationTemplateMapper
{
    public static NotificationEventResponse ToResponse(this NotificationEventDefinition e) => new(
        e.Key, e.Module, e.DisplayName,
        NotificationChannelNames.ToNames(e.SupportedChannels),
        NotificationChannelNames.ToNames(e.DefaultChannels),
        NotificationChannelNames.ToNames(e.RequiredChannels),
        e.AllowedVariables.Select(v => new NotificationVariableResponse(v.Key, v.Type, v.Required)).ToList(),
        e.AllowedRecipientRules,
        e.AllowedActionKeys,
        e.OrchestratorOwned);

    public static NotificationTemplateContent ToContent(this NotificationTemplateContentRequest r) => new(
        r.InAppTitle?.Trim() ?? string.Empty, r.InAppBody ?? string.Empty,
        r.EmailSubject, r.EmailHtml, r.EmailCss, r.EmailText, r.TrayTitle, r.TrayBody,
        string.IsNullOrWhiteSpace(r.ActionKey) ? NotificationActionKeys.OpenRelated : r.ActionKey);

    public static bool IsProductDefault(this NotificationTemplate t) => t.Code == t.EventKey;

    public static NotificationTemplateVersionResponse ToResponse(this NotificationTemplateVersion v) => new(
        v.Id, v.Version, v.Status, v.Revision, v.InAppTitle, v.InAppBody, v.EmailSubject, v.EmailHtml, v.EmailCss,
        v.EmailText, v.TrayTitle, v.TrayBody, v.ActionKey, v.CreatedAt, v.CreatedByUserId, v.UpdatedAt,
        v.PublishedAt, v.PublishedByUserId);

    public static NotificationTemplateDetailResponse ToDetail(
        this NotificationTemplate t, IReadOnlyList<NotificationTemplateVersion> versions)
    {
        var ordered = versions.OrderByDescending(v => v.Version).ToList();
        return new NotificationTemplateDetailResponse(
            t.Id, t.Code, t.EventKey, t.Name, t.Locale, t.IsProductDefault(),
            ordered.FirstOrDefault(v => v.Id == t.ActiveVersionId)?.ToResponse(),
            ordered.FirstOrDefault(v => v.Status == NotificationTemplateVersionStatus.Draft)?.ToResponse(),
            ordered.Select(v => new NotificationTemplateVersionSummaryResponse(
                v.Id, v.Version, v.Status, v.CreatedAt, v.PublishedAt, v.PublishedByUserId)).ToList());
    }

    public static RenderedNotificationResponse ToResponse(this RenderedNotificationContent c) => new(
        c.InAppTitle, c.InAppBody, c.EmailSubject, c.EmailHtml, c.EmailText, c.TrayTitle, c.TrayBody, c.ActionKey);

    /// <summary>Readable stand-in values for preview and test sends - never real business data.</summary>
    public static Dictionary<string, string> SampleVariables(
        NotificationEventDefinition definition, IReadOnlyDictionary<string, string>? overrides)
    {
        var sample = definition.AllowedVariables.ToDictionary(v => v.Key, v => $"[{v.Key}]", StringComparer.Ordinal);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string>())
        {
            if (sample.ContainsKey(key))
                sample[key] = value;
        }
        return sample;
    }
}
