using FluentValidation.TestHelper;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.CreateNotificationTemplate;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SaveNotificationTemplateDraft;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.Commands.SendTestNotification;
using ONEVO.Application.Features.SharedPlatform.Notifications.Management.DTOs;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.ResetNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Settings.Commands.UpdateNotificationSetting;
using ONEVO.Application.Features.SharedPlatform.Notifications.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: notification templates and settings.</summary>
public sealed class NotificationValidatorTests
{
    private static NotificationTemplateContentRequest Content(int title = 10, int body = 10, int html = 10, int action = 10) =>
        new(S(title), S(body), S(title), "<style>p{}</style>" + S(html), "p{}", S(body), S(title), S(body), S(action));

    [Fact]
    public void Create_AtLimits_Passes_EmailHtmlMayHoldStyle()
        => new CreateNotificationTemplateCommandValidator()
            .TestValidate(new CreateNotificationTemplateCommand(S(100), S(150), Content(255, 10_000, 1_000, 50)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Create_OverLimits_Fails()
    {
        var result = new CreateNotificationTemplateCommandValidator()
            .TestValidate(new CreateNotificationTemplateCommand(S(101), S(151), Content(256, 10_001, 200_000, 51)));
        result.ShouldHaveValidationErrorFor(x => x.EventKey);
        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor("Content.InAppTitle");
        result.ShouldHaveValidationErrorFor("Content.InAppBody");
        result.ShouldHaveValidationErrorFor("Content.EmailHtml");
        result.ShouldHaveValidationErrorFor("Content.ActionKey");
    }

    [Fact]
    public void SaveDraft_UsesTheSameContentRules()
    {
        new SaveNotificationTemplateDraftCommandValidator().TestValidate(new SaveNotificationTemplateDraftCommand(Guid.NewGuid(), Content(), 1))
            .ShouldNotHaveAnyValidationErrors();
        new SaveNotificationTemplateDraftCommandValidator().TestValidate(new SaveNotificationTemplateDraftCommand(Guid.NewGuid(), Content(title: 256), 1))
            .ShouldHaveValidationErrorFor("Content.TrayTitle");
    }

    [Fact]
    public void SendTest_BoundsChannelAndVariables()
    {
        var validator = new SendTestNotificationCommandValidator();
        validator.TestValidate(new SendTestNotificationCommand(Guid.NewGuid(), "email", new Dictionary<string, string> { ["name"] = S(2_000) }))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new SendTestNotificationCommand(Guid.NewGuid(), "email", new Dictionary<string, string> { ["name"] = S(2_001) }))
            .ShouldHaveValidationErrorFor(x => x.Variables);
        validator.TestValidate(new SendTestNotificationCommand(Guid.NewGuid(), "email",
                Enumerable.Range(0, 101).ToDictionary(i => $"k{i}", _ => "v")))
            .ShouldHaveValidationErrorFor(x => x.Variables);
        validator.TestValidate(new SendTestNotificationCommand(Guid.NewGuid(), S(101), null)).ShouldHaveValidationErrorFor(x => x.Channel);
    }

    [Fact]
    public void Settings_BoundKeysChannelsAndRule()
    {
        new ResetNotificationSettingCommandValidator().TestValidate(new ResetNotificationSettingCommand(S(101))).ShouldHaveValidationErrorFor(x => x.EventKey);
        new UpdateNotificationSettingCommandValidator()
            .TestValidate(new UpdateNotificationSettingCommand("leave.approved", ["in_app", "email"], null, "manager", 1))
            .ShouldNotHaveAnyValidationErrors();
        var result = new UpdateNotificationSettingCommandValidator()
            .TestValidate(new UpdateNotificationSettingCommand(S(101), Strings(11), null, S(51), 1));
        result.ShouldHaveValidationErrorFor(x => x.EventKey);
        result.ShouldHaveValidationErrorFor(x => x.EnabledChannels);
        result.ShouldHaveValidationErrorFor(x => x.RecipientRule);
    }
}
