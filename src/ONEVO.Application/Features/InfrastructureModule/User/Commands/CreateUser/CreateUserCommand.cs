namespace ONEVO.Application.Features.InfrastructureModule.User.Commands.CreateUser;

public sealed record CreateUserCommand(
    Guid TenantId,
    string Email,
    string PasswordHash,
    string FirstName,
    string LastName,
    Guid CreatedById);
