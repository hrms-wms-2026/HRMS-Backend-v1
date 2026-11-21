using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Conversations.Validation;

namespace ONEVO.Application.Features.Inbox.Chats;

// Task 19. Required-name and "add at least one person" checks stay in InboxChatHandlers (422 with the UI's wording);
// these validators bound what reaches the handler. Group names live in system event JSON and are capped by
// PeopleChatNames.MaxGroupName; descriptions are conversations.description nvarchar(500).

public sealed class CreateGroupChatCommandValidator : AbstractValidator<CreateGroupChatCommand>
{
    public CreateGroupChatCommandValidator()
    {
        RuleFor(x => x.Name).ValidName(PeopleChatNames.MaxGroupName, required: false);
        RuleFor(x => x.EmployeeIds).ValidIdList(ConversationInputLimits.MaxPeoplePerRequest);
    }
}

public sealed class RenameGroupChatCommandValidator : AbstractValidator<RenameGroupChatCommand>
{
    public RenameGroupChatCommandValidator()
    {
        RuleFor(x => x.Name).ValidName(PeopleChatNames.MaxGroupName, required: false);
    }
}

public sealed class SetChatDescriptionCommandValidator : AbstractValidator<SetChatDescriptionCommand>
{
    public SetChatDescriptionCommandValidator()
    {
        RuleFor(x => x.Description).ValidName(ChatInfoHandlers.MaxDescription, required: false);
    }
}

public sealed class StartCaseGroupCommandValidator : AbstractValidator<StartCaseGroupCommand>
{
    public StartCaseGroupCommandValidator()
    {
        RuleFor(x => x.Source).ValidName(ConversationInputLimits.SubjectPartMaxLength);
        RuleFor(x => x.Kind).ValidName(ConversationInputLimits.SubjectPartMaxLength);
        RuleFor(x => x.EmployeeIds).ValidIdList(ConversationInputLimits.MaxPeoplePerRequest);
        RuleFor(x => x.Name).ValidName(PeopleChatNames.MaxGroupName, required: false);
    }
}

public sealed class ForwardCaseCommandValidator : AbstractValidator<ForwardCaseCommand>
{
    public ForwardCaseCommandValidator()
    {
        RuleFor(x => x.Source).ValidName(ConversationInputLimits.SubjectPartMaxLength);
        RuleFor(x => x.Kind).ValidName(ConversationInputLimits.SubjectPartMaxLength);
        RuleFor(x => x.ConversationIds).ValidIdList(ConversationInputLimits.MaxForwardTargets);
    }
}

public sealed class ForwardMessageCommandValidator : AbstractValidator<ForwardMessageCommand>
{
    public ForwardMessageCommandValidator()
    {
        RuleFor(x => x.ConversationIds).ValidIdList(ConversationInputLimits.MaxForwardTargets);
    }
}
