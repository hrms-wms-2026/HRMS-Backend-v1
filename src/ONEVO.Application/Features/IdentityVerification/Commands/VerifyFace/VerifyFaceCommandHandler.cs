using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;
using ONEVO.Application.Features.IdentityVerification.Services;
using ONEVO.Application.Features.Storage.File.Helpers;
using ONEVO.Application.Features.Storage.File.RepositoryInterfaces;
using ONEVO.Application.Features.Storage.File.ServiceInterfaces;
using ONEVO.Application.Features.Users.RepositoryInterfaces;
using ONEVO.Domain.Features.IdentityVerification.Entities;

namespace ONEVO.Application.Features.IdentityVerification.Commands.VerifyFace;

public sealed class VerifyFaceCommandHandler
    : IRequestHandler<VerifyFaceCommand, Result<VerifyFaceResponse>>
{
    private const int MaximumImageBytes = 5 * 1024 * 1024;

    private readonly IAgentGatewayRepository _agents;
    private readonly IUserProfileRepository _profiles;
    private readonly IVerificationRepository _verification;
    private readonly IIdentityImageValidator _images;
    private readonly IFaceComparisonService _faces;
    private readonly IFileRecordRepository _fileRecords;
    private readonly IObjectStorageAdapter _objectStorage;
    private readonly IFileStorageService _files;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public VerifyFaceCommandHandler(
        IAgentGatewayRepository agents,
        IUserProfileRepository profiles,
        IVerificationRepository verification,
        IIdentityImageValidator images,
        IFaceComparisonService faces,
        IFileRecordRepository fileRecords,
        IObjectStorageAdapter objectStorage,
        IFileStorageService files,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _agents = agents;
        _profiles = profiles;
        _verification = verification;
        _images = images;
        _faces = faces;
        _fileRecords = fileRecords;
        _objectStorage = objectStorage;
        _files = files;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result<VerifyFaceResponse>> Handle(
        VerifyFaceCommand request,
        CancellationToken cancellationToken)
    {
        var trigger = request.Trigger.Trim().ToLowerInvariant();
        if (trigger is not ("clock_in" or "clock_out"))
        {
            return Result<VerifyFaceResponse>.Failure(
                "Face verification trigger must be clock_in or clock_out.",
                400);
        }

        var agent = await _agents.GetAgentByIdAsync(
            request.AgentId,
            cancellationToken);
        if (agent is null ||
            agent.EmployeeId is null ||
            !string.Equals(agent.Status, "active", StringComparison.Ordinal))
        {
            return Result<VerifyFaceResponse>.Forbidden(
                "Agent is not an approved active device.");
        }

        var employee = await _profiles.GetEmployeeByIdAsync(
            agent.EmployeeId.Value,
            cancellationToken);
        if (employee is null || employee.TenantId != agent.TenantId)
            return Result<VerifyFaceResponse>.NotFound("Employee not found.");

        var policy = await _verification.GetActivePolicyAsync(
            cancellationToken);
        if (policy is null ||
            policy.TenantId != agent.TenantId ||
            !policy.IsActive ||
            !policy.CameraPhotoVerificationEnabled)
        {
            return Result<VerifyFaceResponse>.Conflict(
                "Camera photo verification is not enabled by Company policy.");
        }

        var reference = await _verification.GetActiveReferencePhotoAsync(
            employee.Id,
            cancellationToken);
        if (reference is null ||
            reference.TenantId != agent.TenantId ||
            !reference.IsActive ||
            !string.Equals(reference.Status, "approved", StringComparison.Ordinal))
        {
            return Result<VerifyFaceResponse>.Conflict(
                "An approved reference photo is required.");
        }

        var candidateValidation = await _images.ValidateAsync(
            request.Content,
            request.FileName,
            request.ContentType,
            MaximumImageBytes,
            cancellationToken);
        if (!candidateValidation.IsSuccess)
        {
            return Result<VerifyFaceResponse>.Failure(
                candidateValidation.Error ?? "Identity image is invalid.",
                candidateValidation.StatusCode ?? 400);
        }

        var detection = await _faces.DetectFacesAsync(
            candidateValidation.Value!,
            cancellationToken);
        if (!detection.ProviderAvailable)
        {
            return Result<VerifyFaceResponse>.Failure(
                detection.FailureCode ?? "face_provider_unavailable",
                503);
        }
        if (detection.FaceCount != 1)
        {
            return Result<VerifyFaceResponse>.Failure(
                detection.FaceCount == 0
                    ? "exactly_one_face_required:no_face"
                    : "exactly_one_face_required:multiple_faces",
                400);
        }

        var referenceFile = await _fileRecords.GetByIdAsync(
            agent.TenantId,
            reference.PhotoFileId,
            cancellationToken);
        if (referenceFile is null)
        {
            return Result<VerifyFaceResponse>.Conflict(
                "Reference photo content is unavailable.");
        }

        byte[] referenceBytes;
        await using (var referenceStream =
            await _objectStorage.GetObjectAsync(
                referenceFile.StorageKey,
                cancellationToken))
        {
            if (referenceStream.CanSeek &&
                referenceStream.Length > MaximumImageBytes)
            {
                return Result<VerifyFaceResponse>.Conflict(
                    "Reference photo content is invalid.");
            }
            using var buffer = new MemoryStream();
            await referenceStream.CopyToAsync(
                buffer,
                cancellationToken);
            if (buffer.Length is <= 0 or > MaximumImageBytes)
            {
                return Result<VerifyFaceResponse>.Conflict(
                    "Reference photo content is invalid.");
            }
            referenceBytes = buffer.ToArray();
        }

        var comparison = await _faces.CompareFacesAsync(
            referenceBytes,
            candidateValidation.Value!,
            policy.MatchThreshold,
            cancellationToken);
        if (!comparison.ProviderAvailable)
        {
            return Result<VerifyFaceResponse>.Failure(
                comparison.FailureCode ?? "face_provider_unavailable",
                503);
        }

        await using var uploadStream =
            new MemoryStream(candidateValidation.Value!, writable: false);
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
            return Result<VerifyFaceResponse>.Failure(
                upload.Error ?? "Verification photo storage failed.",
                upload.StatusCode ?? 500);
        }

        var now = _clock.UtcNow;
        var matched = comparison.Similarity >= policy.MatchThreshold;
        var record = new VerificationRecord
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = employee.Id,
            VerifiedAt = now,
            Method = "photo",
            MatchConfidence = Math.Round(comparison.Similarity, 2),
            Status = matched ? "verified" : "failed",
            AgentId = agent.Id,
            FailureReason = matched ? null : "below_match_threshold",
            Trigger = trigger,
            SubmittedAt = now,
            CreatedAt = now
        };
        var evidence = new VerificationEvidenceAsset
        {
            Id = Guid.NewGuid(),
            TenantId = agent.TenantId,
            EmployeeId = employee.Id,
            VerificationRecordId = record.Id,
            FileRecordId = upload.Value!.Id,
            EvidenceType = matched
                ? trigger == "clock_in"
                    ? "clock_in_photo"
                    : "clock_out_photo"
                : "verification_failure_photo",
            TriggerType = trigger,
            CapturedAt = now,
            AgentId = agent.Id,
            Metadata = JsonSerializer.Serialize(new
            {
                provider = "amazon_rekognition",
                threshold = policy.MatchThreshold
            }),
            CreatedAt = now
        };
        await _verification.AddVerificationRecordAsync(
            record,
            cancellationToken);
        await _verification.AddEvidenceAssetAsync(
            evidence,
            cancellationToken);
        await _uow.SaveChangesAsync(cancellationToken);

        return Result<VerifyFaceResponse>.Success(
            new VerifyFaceResponse(
                record.Id,
                record.Status,
                record.MatchConfidence,
                now));
    }
}
