using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Monitoring.Biometrics.DTOs.Responses;

namespace ONEVO.Application.Features.Monitoring.Biometrics.Commands.CreateEnrollmentAttempt;

public record CreateEnrollmentAttemptCommand : IRequest<Result<EnrollmentAttemptResponse>>;
