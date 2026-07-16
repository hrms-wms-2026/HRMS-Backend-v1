using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.ServiceInterfaces;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.VerifyGatewayCredentials;

/// <summary>
/// Verifies gateway credentials against the live provider — does NOT persist credentials.
/// UI must show the verified account details before enabling Save.
/// </summary>
public sealed record VerifyGatewayCredentialsCommand(
    string Provider,
    /// <summary>Credentials passed by caller; used for HTTP verification only; never logged.</summary>
    Dictionary<string, string> Credentials) : IRequest<Result<GatewayVerificationResult>>;

public sealed class VerifyGatewayCredentialsCommandHandler
    : IRequestHandler<VerifyGatewayCredentialsCommand, Result<GatewayVerificationResult>>
{
    private readonly IPaymentGatewayVerificationService _verificationService;

    public VerifyGatewayCredentialsCommandHandler(IPaymentGatewayVerificationService verificationService)
        => _verificationService = verificationService;

    public async Task<Result<GatewayVerificationResult>> Handle(
        VerifyGatewayCredentialsCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Provider))
            return Result<GatewayVerificationResult>.Failure("Provider is required.", 400);

        if (request.Credentials is null || request.Credentials.Count == 0)
            return Result<GatewayVerificationResult>.Failure("Credentials are required for verification.", 400);

        // SECURITY: credentials are used only for the HTTP call; they are NOT logged here
        var result = await _verificationService.VerifyAsync(
            request.Provider,
            request.Credentials,
            cancellationToken);

        return Result<GatewayVerificationResult>.Success(result);
    }
}
