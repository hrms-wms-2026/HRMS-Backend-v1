using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Application.Features.Auth.Login.Commands.MfaConfirmSetup;

public class ConfirmMfaSetupCommandHandler : IRequestHandler<ConfirmMfaSetupCommand, Result>
{
    private readonly IUserMfaRepository _userMfas;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IEncryptionService _encryption;
    private readonly ITotpService _totpService;

    public ConfirmMfaSetupCommandHandler(
        IUserMfaRepository userMfas,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IEncryptionService encryption,
        ITotpService totpService)
    {
        _userMfas = userMfas;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _encryption = encryption;
        _totpService = totpService;
    }

    public async Task<Result> Handle(ConfirmMfaSetupCommand request, CancellationToken cancellationToken)
    {
        var pending = await _userMfas.GetTotpAsync(_currentUser.UserId, isVerified: false, cancellationToken);
        if (pending is null)
            return Result.Failure("No pending MFA setup exists.", 400);

        var secret = _encryption.Decrypt(pending.Secret);
        if (!_totpService.Verify(secret, request.Code))
            return Result.Failure("Invalid MFA code.", 400);

        pending.IsVerified = true;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
