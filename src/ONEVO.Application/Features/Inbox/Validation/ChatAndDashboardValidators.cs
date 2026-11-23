using FluentValidation;
using ONEVO.Application.Common.Helpers;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Commands.OpenConversation;
using ONEVO.Application.Features.Conversations.Commands.ReactToConversationMessage;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Dashboard.Layout.Commands.AddDashboardWidget;
using ONEVO.Application.Features.Dashboard.Layout.Commands.SaveDashboardLayout;
using ONEVO.Application.Features.Inbox.Chats;
using ONEVO.Application.Features.Inbox.Commands.ExecuteInboxAction;

namespace ONEVO.Application.Features.Inbox.Validation;

/// <summary>
/// Task 19b. Chats, the inbox action endpoint and the dashboard layout.
/// <list type="bullet">
/// <item>conversations.subject_source and subject_kind are nvarchar(40), the bound task 19 used for
/// StartCaseGroup and ForwardCase. An inbox action name is only compared with the provider's known actions.</item>
/// <item>Reaction emoji are nvarchar(32) (ConversationRules.MaxEmojiLength). A chat icon is nvarchar(64)
/// (EntityIconRules.MaxLength) and its colour nvarchar(9).</item>
/// <item>Dashboard widget keys are nvarchar(100) and breakpoints nvarchar(20). A saved layout holds at most 200
/// widgets, the grid's row count.</item>
/// </list>
/// The handlers keep the known sources, kinds, actions, icons, widgets and breakpoints.
/// </summary>
public static class ChatAndDashboardLimits
{
    public const int SubjectMaxLength = 40;
    public const int EmojiMaxLength = ConversationRules.MaxEmojiLength;
    public const int IconMaxLength = EntityIconRules.MaxLength;
    public const int ColorMaxLength = 9;
    public const int WidgetKeyMaxLength = 100;
    public const int BreakpointMaxLength = 20;
    public const int MaxLayoutWidgets = 200;
}

public sealed class OpenConversationCommandValidator : AbstractValidator<OpenConversationCommand>
{
    public OpenConversationCommandValidator()
    {
        RuleFor(x => x.Source).ValidName(ChatAndDashboardLimits.SubjectMaxLength, required: false);
        RuleFor(x => x.Kind).ValidName(ChatAndDashboardLimits.SubjectMaxLength, required: false);
    }
}

public sealed class ExecuteInboxActionCommandValidator : AbstractValidator<ExecuteInboxActionCommand>
{
    public ExecuteInboxActionCommandValidator()
    {
        RuleFor(x => x.Source).ValidName(ChatAndDashboardLimits.SubjectMaxLength, required: false);
        RuleFor(x => x.Kind).ValidName(ChatAndDashboardLimits.SubjectMaxLength, required: false);
        RuleFor(x => x.Action).ValidName(InputLimits.KeyMaxLength, required: false);
    }
}

public sealed class ReactToConversationMessageCommandValidator : AbstractValidator<ReactToConversationMessageCommand>
{
    public ReactToConversationMessageCommandValidator()
    {
        RuleFor(x => x.Emoji).ValidName(ChatAndDashboardLimits.EmojiMaxLength, required: false);
    }
}

public sealed class SetChatIconCommandValidator : AbstractValidator<SetChatIconCommand>
{
    public SetChatIconCommandValidator()
    {
        RuleFor(x => x.Icon).ValidName(ChatAndDashboardLimits.IconMaxLength, required: false);
        RuleFor(x => x.Color).ValidName(ChatAndDashboardLimits.ColorMaxLength, required: false);
    }
}

public sealed class AddDashboardWidgetCommandValidator : AbstractValidator<AddDashboardWidgetCommand>
{
    public AddDashboardWidgetCommandValidator()
    {
        RuleFor(x => x.WidgetKey).ValidName(ChatAndDashboardLimits.WidgetKeyMaxLength, required: false);
    }
}

public sealed class SaveDashboardLayoutCommandValidator : AbstractValidator<SaveDashboardLayoutCommand>
{
    public SaveDashboardLayoutCommandValidator()
    {
        RuleFor(x => x.Breakpoint).ValidName(ChatAndDashboardLimits.BreakpointMaxLength, required: false);
        RuleFor(x => x.Widgets)
            .Must(w => w is null || w.Count <= ChatAndDashboardLimits.MaxLayoutWidgets)
            .WithMessage($"'{{PropertyName}}' can hold at most {ChatAndDashboardLimits.MaxLayoutWidgets} entries.");
    }
}
