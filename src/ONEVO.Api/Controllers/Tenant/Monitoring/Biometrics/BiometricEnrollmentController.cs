using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CompleteEnrollmentAttempt;
using ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

namespace ONEVO.Api.Controllers.Tenant.Monitoring.Biometrics;

/// <summary>
/// Backend counterpart to the already-built TrayApp/Service AWS Rekognition Face Liveness
/// flow (BiometricEnrollmentViewModel -> EnrollmentCoordinator -> OnevoApiClient). Video never
/// reaches this backend - the client streams directly to AWS using the STS credentials
/// returned by CreateAttempt; this backend only creates the session and later asks AWS
/// for the authoritative result. Auth: Bearer Device JWT, same as other tray-device endpoints.
/// </summary>
[ApiController]
[Route("api/v1/monitoring/biometrics")]
[Authorize(Policy = "TrayDevicePolicy")]
public class BiometricEnrollmentController : ControllerBase
{
    private readonly IMediator _mediator;

    public BiometricEnrollmentController(IMediator mediator) => _mediator = mediator;

    [HttpPost("enrollment-attempts")]
    public async Task<IActionResult> CreateAttempt(CancellationToken ct)
    {
        var result = await _mediator.Send(new CreateEnrollmentAttemptCommand(), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var v = result.Value!;
        return Ok(new
        {
            attempt_id = v.AttemptId,
            aws_session_id = v.AwsSessionId,
            region = v.Region,
            challenge_type = v.ChallengeType,
            access_key_id = v.AccessKeyId,
            secret_access_key = v.SecretAccessKey,
            session_token = v.SessionToken,
            credentials_expire_at = v.CredentialsExpireAt
        });
    }

    [HttpPost("enrollment-attempts/{attemptId:guid}/complete")]
    public async Task<IActionResult> CompleteAttempt(Guid attemptId, CancellationToken ct)
    {
        var result = await _mediator.Send(new CompleteEnrollmentAttemptCommand(attemptId), ct);
        if (!result.IsSuccess)
            return Problem(result.Error, statusCode: result.StatusCode ?? 400);

        var v = result.Value!;
        return Ok(new { profile_id = v.ProfileId, status = v.Status, enrolled_at = v.EnrolledAt });
    }
}
