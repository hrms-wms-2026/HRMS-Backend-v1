using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.Services;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Application.Features.IdentityVerification.Commands.EnrollReferencePhoto;

public sealed class EnrollReferencePhotoCommandHandler
    : IRequestHandler<
        EnrollReferencePhotoCommand,
        Result<EnrollReferencePhotoResponse>>
{
    private const int MaximumImageBytes = 5 * 1024 * 1024;

    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;
    private readonly IIdentityImageValidator _images;
    private readonly IFaceComparisonService _faces;
    private readonly IFileStorageService _files;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public EnrollReferencePhotoCommandHandler(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        IVerificationRepository verification,
        IIdentityImageValidator images,
        IFaceComparisonService faces,
        IFileStorageService files,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _profiles = profiles;
        _verification = verification;
        _images = images;
        _faces = faces;
        _files = files;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<EnrollReferencePhotoResponse>> Handle(
        EnrollReferencePhotoCommand request,
        CancellationToken cancellationToken)
    {
        var noticeVersion = request.NoticeVersion.Trim();
        if (noticeVersion.Length is < 1 or > 50)
        {
            return Result<EnrollReferencePhotoResponse>.Failure(
                "A valid biometric consent notice version is required.",
                400);
        }

        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<EnrollReferencePhotoResponse>.Forbidden(
                "Agent is not an approved active device.");
        }

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<EnrollReferencePhotoResponse>.NotFound("Employee not found.");

        var policy = await _verification.GetActivePolicyAsync(
            cancellationToken);
        if (policy is null ||
            policy.TenantId != agent.TenantId ||
            !policy.IsActive ||
            !policy.CameraPhotoVerificationEnabled)
        {
            return Result<EnrollReferencePhotoResponse>.Conflict(
                "Camera photo verification is not enabled by Company policy.");
        }

        var existing = await _verification.GetActiveReferencePhotoAsync(
            employee.Id,
            cancellationToken);
        if (existing is not null)
        {
            return Result<EnrollReferencePhotoResponse>.Conflict(
                "An approved reference photo already exists.");
        }

        var validation = await _images.ValidateAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            MaximumImageBytes,
            cancellationToken);
        if (!validation.IsSuccess)
        {
            return Result<EnrollReferencePhotoResponse>.Failure(
                validation.Error ?? "Identity image is invalid.",
                validation.StatusCode ?? 400);
        }

        var detection = await _faces.DetectFacesAsync(
            validation.Value!,
            cancellationToken);
        if (!detection.ProviderAvailable)
        {
            return Result<EnrollReferencePhotoResponse>.Failure(
                detection.FailureCode ?? "face_provider_unavailable",
                503);
        }
        if (detection.FaceCount != 1)
        {
            return Result<EnrollReferencePhotoResponse>.Failure(
                detection.FaceCount == 0
                    ? "exactly_one_face_required:no_face"
                    : "exactly_one_face_required:multiple_faces",
                400);
        }

        await using var uploadStream =
            new MemoryStream(validation.Value!, writable: false);
        var upload = await _files.UploadAsync(
            agent.TenantId,
            employee.UserId,
            request.FileName,
            request.ContentType.ToLowerInvariant(),
            UploadPurposeCatalog.IdentityVerificationPhoto,
            uploadStream,
            cancellationToken);
        if (!upload.IsSuccess)
        {
            return Result<EnrollReferencePhotoResponse>.Failure(
                upload.Error ?? "Reference photo storage failed.",
                upload.StatusCode ?? 500);
        }

        var now = _clock.UtcNow;
        var consent = new GdprConsentRecord
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            UserId = employee.UserId,
            ConsentType = "biometric",
            Consented = true,
            ConsentedAt = now,
            NoticeVersion = noticeVersion,
            CapturedAgentId = agent.Id
        };
        var autoApprove = string.Equals(
            policy.ReferenceEnrollmentMode,
            "trusted_sso_auto_approve",
            StringComparison.Ordinal);
        var reference = new VerificationReferencePhoto
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = employee.Id,
            PhotoFileId = upload.Value!.Id,
            Source = "agent_first_sign_in",
            Status = autoApprove ? "approved" : "pending_review",
            CapturedDeviceId = agent.Id,
            CapturedAt = now,
            LegalAcceptanceRecordId = consent.Id,
            IsActive = autoApprove,
            CreatedAt = now
        };
        await _verification.AddConsentAsync(
            consent,
            cancellationToken);
        await _verification.AddReferencePhotoAsync(
            reference,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<EnrollReferencePhotoResponse>.Success(
            new EnrollReferencePhotoResponse(
                reference.Id,
                reference.Status,
                reference.IsActive,
                now));
    }
}

