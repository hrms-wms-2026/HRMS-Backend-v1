using FluentValidation;

namespace ONEVO.Application.Features.Auth.Login.Commands.SelectWorkspace;

public sealed class SelectWorkspaceCommandValidator : AbstractValidator<SelectWorkspaceCommand>
{
    public SelectWorkspaceCommandValidator()
    {
        RuleFor(c => c.LoginChallenge).NotEmpty().MaximumLength(512);
        RuleFor(c => c.Workspace).NotEmpty().MaximumLength(100);
    }
}
