using MediatR;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Application.Common.Models;

using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.DTOs.Responses;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Auth.Login.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<LoginResponseDto>>
{
    private const string GenericCredentialFailureMessage = "Invalid email or password.";
    private const string LegalChallengeOrigin = "password";

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILoginContinuationService _continuation;
    private readonly ITenantContext _tenantContext;

    public LoginCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ILoginContinuationService continuation,
        ITenantContext tenantContext)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _continuation = continuation;
        _tenantContext = tenantContext;
    }

    public async Task<Result<LoginResponseDto>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved || _tenantContext.ContextMode != TenantContextMode.Tenant)
            return Result<LoginResponseDto>.Failure("Tenant context is not resolved.", 400);

        if (_tenantContext.Status is not (TenantStatus.Active or TenantStatus.Trial))
            return Result<LoginResponseDto>.Failure("This tenant is not available.", 403);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _users.GetByTenantAndEmailAsync(_tenantContext.TenantId, normalizedEmail, cancellationToken);

        if (user is null)
            return Result<LoginResponseDto>.Failure(GenericCredentialFailureMessage);

        if (!user.IsActive)
            return Result<LoginResponseDto>.Failure("Account is deactivated. Contact your administrator.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            return Result<LoginResponseDto>.Failure(GenericCredentialFailureMessage);

        var continuationRequest = new LoginContinuationRequest(
            _tenantContext.TenantId,
            user.Id,
            SwitchTenantContext: false,
            GenericFailureMessage: GenericCredentialFailureMessage,
            LegalChallengeOrigin: LegalChallengeOrigin,
            IpAddress: request.IpAddress,
            UserAgent: request.UserAgent);

        return await _continuation.ContinueAsync(continuationRequest, cancellationToken);
    }
}
