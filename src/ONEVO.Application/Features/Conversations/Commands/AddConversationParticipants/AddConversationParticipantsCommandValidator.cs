using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.Conversations.Validation;

namespace ONEVO.Application.Features.Conversations.Commands.AddConversationParticipants;

public sealed class AddConversationParticipantsCommandValidator : AbstractValidator<AddConversationParticipantsCommand>
{
    public AddConversationParticipantsCommandValidator()
    {
        // "Add at least one person" stays with the handler (422).
        RuleFor(x => x.EmployeeIds).ValidIdList(ConversationInputLimits.MaxPeoplePerRequest);
    }
}
