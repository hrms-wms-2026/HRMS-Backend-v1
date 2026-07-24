using System.Security.Cryptography;
using System.Text;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

public class ConfirmEnrollmentCommandHandler : IRequestHandler<ConfirmEnrollmentCommand, Result<ConfirmEnrollmentResult>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _uow;

    public ConfirmEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IUnitOfWork uow)
    {
        _repo = repo;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result<ConfirmEnrollmentResult>> Handle(
        ConfirmEnrollmentCommand request, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return Result<ConfirmEnrollmentResult>.Failure("Tenant context is not resolved.", 400);

        var challenge = await _repo.GetChallengeByIdAsync(request.EnrollmentId, cancellationToken);

        if (challenge is null)
            return Result<ConfirmEnrollmentResult>.NotFound("Enrollment challenge not found.");

        if (challenge.Status != "pending")
            return Result<ConfirmEnrollmentResult>.Failure("Enrollment challenge is no longer pending.", 409);

        if (challenge.ExpiresAt < DateTimeOffset.UtcNow)
            return Result<ConfirmEnrollmentResult>.Failure("Enrollment challenge has expired.", 400);

        var plainCode = GenerateAuthCode();
        var codeHash = HashCode(plainCode);

        var confirmed = await _repo.TryMarkChallengeConfirmedAsync(
            request.EnrollmentId,
            codeHash,
            _tenantContext.TenantId,
            _currentUser.UserId,
            _currentUser.UserId,
            cancellationToken);

        if (!confirmed)
            return Result<ConfirmEnrollmentResult>.Conflict("Enrollment challenge was already confirmed.");

        return Result<ConfirmEnrollmentResult>.Success(new ConfirmEnrollmentResult(plainCode, challenge.RedirectUri));
    }

    private static string GenerateAuthCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
