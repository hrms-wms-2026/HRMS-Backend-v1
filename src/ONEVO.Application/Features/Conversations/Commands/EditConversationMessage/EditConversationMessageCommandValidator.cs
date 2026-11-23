using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Conversations.Validation;

namespace ONEVO.Application.Features.Conversations.Commands.EditConversationMessage;

public sealed class EditConversationMessageCommandValidator : AbstractValidator<EditConversationMessageCommand>
{
    public EditConversationMessageCommandValidator()
    {
        RuleFor(x => x.BodyHtml).ValidRichText(ConversationRules.MaxBodyLength);
        RuleFor(x => x.MentionEmployeeIds).ValidIdList(ConversationInputLimits.MaxPeoplePerRequest);
    }
}
