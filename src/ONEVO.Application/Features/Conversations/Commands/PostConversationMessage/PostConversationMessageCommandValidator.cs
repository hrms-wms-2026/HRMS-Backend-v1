using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Services;
using ONEVO.Application.Features.Conversations.Validation;

namespace ONEVO.Application.Features.Conversations.Commands.PostConversationMessage;

public sealed class PostConversationMessageCommandValidator : AbstractValidator<PostConversationMessageCommand>
{
    public PostConversationMessageCommandValidator()
    {
        // Emptiness stays with the handler: a message may be empty when it carries files.
        RuleFor(x => x.BodyHtml).ValidRichText(ConversationRules.MaxBodyLength);
        RuleFor(x => x.MentionEmployeeIds).ValidIdList(ConversationInputLimits.MaxPeoplePerRequest);
        RuleFor(x => x.AttachmentFileIds).ValidIdList(PostConversationMessageCommandHandler.MaxAttachments);
        RuleFor(x => x.Tag).ValidName(ConversationInputLimits.TagMaxLength, required: false);
    }
}
