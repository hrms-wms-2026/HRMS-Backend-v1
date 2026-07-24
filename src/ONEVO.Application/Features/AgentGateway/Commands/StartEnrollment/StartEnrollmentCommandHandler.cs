using MediatR;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.DTOs;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Domain.Features.AgentGateway.Entities;

namespace ONEVO.Application.Features.AgentGateway.Commands.StartEnrollment;

public class StartEnrollmentCommandHandler
    : IRequestHandler<StartEnrollmentCommand, Result<EnrollStartResponseDto>>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly string _appBaseUrl;

    public StartEnrollmentCommandHandler(
        IAgentGatewayRepository repo,
        IUnitOfWork uow,
        IConfiguration configuration)
    {
        _repo = repo;
        _uow = uow;
        _appBaseUrl = (configuration["Urls:AppBaseUrl"] ?? "https://app.onevo.io").TrimEnd('/');
    }

    public async Task<Result<EnrollStartResponseDto>> Handle(
        StartEnrollmentCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return Result<EnrollStartResponseDto>.Failure("device_id is required.", 400);

        if (request.RedirectUri is not null)
        {
            if (!Uri.TryCreate(request.RedirectUri, UriKind.Absolute, out var parsedUri)
                || parsedUri.Scheme != "http"
                || !parsedUri.IsLoopback)
            {
                return Result<EnrollStartResponseDto>.Failure(
                    "redirect_uri must be an absolute http URL on a loopback address.", 400);
            }
        }

        var enrollmentId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);

        var challenge = new AgentEnrollmentChallenge
        {
            Id = enrollmentId,
            DeviceId = request.DeviceId.Trim(),
            DeviceName = request.DeviceName.Trim(),
            OsVersion = request.OsVersion.Trim(),
            AgentVersion = request.AgentVersion.Trim(),
            Status = "pending",
            ExpiresAt = expiresAt,
            RedirectUri = request.RedirectUri,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _repo.AddChallengeAsync(challenge, cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        var authUrl = $"{_appBaseUrl}/agent/enroll?enrollment_id={enrollmentId}";

        return Result<EnrollStartResponseDto>.Success(
            new EnrollStartResponseDto(enrollmentId, authUrl, expiresAt));

    }
}
