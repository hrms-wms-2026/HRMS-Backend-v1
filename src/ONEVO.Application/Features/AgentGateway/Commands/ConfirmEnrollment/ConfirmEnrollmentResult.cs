namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

public sealed record ConfirmEnrollmentResult(
    string AuthorizationCode,
    string? RedirectUri);
