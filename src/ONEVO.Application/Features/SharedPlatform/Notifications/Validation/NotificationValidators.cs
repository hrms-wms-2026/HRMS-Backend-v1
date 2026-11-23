using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;

namespace ONEVO.Application.Features.SharedPlatform.Notifications.Validation;

/// <summary>
/// Task 19b. notification_templates: event_key nvarchar(100), name nvarchar(150). notification_template_versions:
/// in_app_title, email_subject and tray_title nvarchar(255), action_key nvarchar(50). tenant_notification_rules:
/// event_key nvarchar(100), recipient_rule nvarchar(50). The bodies, email HTML, CSS and text are nvarchar(max);
/// the caps below are chosen well above any real template. The email HTML is a template that may hold style and
/// meta tags, so it gets a length cap only, not the rich-text active-content check; the renderer keeps its own
/// rules. The handlers keep the known event keys, channels, placeholders and required checks.
/// </summary>
public static class NotificationLimits
{
    public const int EventKeyMaxLength = 100;
    public const int NameMaxLength = 150;
    public const int TitleMaxLength = 255;
    public const int ActionKeyMaxLength = 50;
    public const int RecipientRuleMaxLength = 50;
    public const int BodyMaxLength = 10_000;
    public const int EmailHtmlMaxLength = 200_000;
    public const int EmailCssMaxLength = 50_000;
    public const int EmailTextMaxLength = 50_000;
    public const int MaxChannels = 10;
    public const int MaxTestVariables = 100;
    public const int TestVariableValueMaxLength = 2_000;
}

public sealed class NotificationTemplateContentRequestValidator : AbstractValidator<NotificationTemplateContentRequest>
{
    public NotificationTemplateContentRequestValidator()
    {
        RuleFor(x => x.InAppTitle).ValidName(NotificationLimits.TitleMaxLength, required: false);
        RuleFor(x => x.InAppBody).ValidName(NotificationLimits.BodyMaxLength, required: false);
        RuleFor(x => x.EmailSubject).ValidName(NotificationLimits.TitleMaxLength, required: false);
        RuleFor(x => x.EmailHtml).ValidName(NotificationLimits.EmailHtmlMaxLength, required: false);
        RuleFor(x => x.EmailCss).ValidName(NotificationLimits.EmailCssMaxLength, required: false);
        RuleFor(x => x.EmailText).ValidName(NotificationLimits.EmailTextMaxLength, required: false);
        RuleFor(x => x.TrayTitle).ValidName(NotificationLimits.TitleMaxLength, required: false);
        RuleFor(x => x.TrayBody).ValidName(NotificationLimits.BodyMaxLength, required: false);
        RuleFor(x => x.ActionKey).ValidName(NotificationLimits.ActionKeyMaxLength, required: false);
    }
}

public sealed class CreateNotificationTemplateCommandValidator : AbstractValidator<CreateNotificationTemplateCommand>
{
    public CreateNotificationTemplateCommandValidator()
    {
        RuleFor(x => x.EventKey).ValidName(NotificationLimits.EventKeyMaxLength, required: false);
        RuleFor(x => x.Name).ValidName(NotificationLimits.NameMaxLength, required: false);
        RuleFor(x => x.Content).SetValidator(new NotificationTemplateContentRequestValidator());
    }
}

public sealed class SaveNotificationTemplateDraftCommandValidator : AbstractValidator<SaveNotificationTemplateDraftCommand>
{
    public SaveNotificationTemplateDraftCommandValidator()
    {
        RuleFor(x => x.Content).SetValidator(new NotificationTemplateContentRequestValidator());
    }
}

public sealed class SendTestNotificationCommandValidator : AbstractValidator<SendTestNotificationCommand>
{
    public SendTestNotificationCommandValidator()
    {
        RuleFor(x => x.Channel).ValidName(InputLimits.KeyMaxLength, required: false);
        RuleFor(x => x.Variables)
            .Must(v => v is null || v.Count <= NotificationLimits.MaxTestVariables)
            .WithMessage($"'{{PropertyName}}' can hold at most {NotificationLimits.MaxTestVariables} entries.")
            .Must(v => v is null || v.All(p => p.Key.Length <= InputLimits.KeyMaxLength
                && (p.Value is null || p.Value.Length <= NotificationLimits.TestVariableValueMaxLength)))
            .WithMessage($"Each variable name must be {InputLimits.KeyMaxLength} characters or fewer and each value {NotificationLimits.TestVariableValueMaxLength}.");
    }
}

public sealed class ResetNotificationSettingCommandValidator : AbstractValidator<ResetNotificationSettingCommand>
{
    public ResetNotificationSettingCommandValidator()
    {
        RuleFor(x => x.EventKey).ValidName(NotificationLimits.EventKeyMaxLength, required: false);
    }
}

public sealed class UpdateNotificationSettingCommandValidator : AbstractValidator<UpdateNotificationSettingCommand>
{
    public UpdateNotificationSettingCommandValidator()
    {
        RuleFor(x => x.EventKey).ValidName(NotificationLimits.EventKeyMaxLength, required: false);
        RuleFor(x => x.EnabledChannels).ValidStringList(NotificationLimits.MaxChannels, InputLimits.KeyMaxLength);
        RuleFor(x => x.RecipientRule).ValidName(NotificationLimits.RecipientRuleMaxLength, required: false);
    }
}
