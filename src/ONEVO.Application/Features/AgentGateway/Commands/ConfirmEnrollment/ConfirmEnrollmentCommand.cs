using MediatR;
using ONEVO.Application.Common.Models;

namespace ONEVO.Application.Features.AgentGateway.Commands.ConfirmEnrollment;

/// <summary>
/// Called by the web frontend when the authenticated employee confirms "Yes, this is my desktop".
/// Returns a short-lived authorization_code that the TrayApp uses in enroll/complete.
/// </summary>
public record ConfirmEnrollmentCommand(Guid EnrollmentId) : IRequest<Result<string>>;
