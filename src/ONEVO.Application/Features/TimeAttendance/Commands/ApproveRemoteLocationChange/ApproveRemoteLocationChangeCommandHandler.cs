using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ApproveRemoteLocationChange;

public sealed class ApproveRemoteLocationChangeCommandHandler
    : IRequestHandler<ApproveRemoteLocationChangeCommand, Result>
{
    private readonly IVerificationRepository _repository;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ApproveRemoteLocationChangeCommandHandler(
        IVerificationRepository repository,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _repository = repository;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(
        ApproveRemoteLocationChangeCommand request,
        CancellationToken cancellationToken)
    {
        var change = await _repository.GetRemoteChangeRequestAsync(
            request.RequestId,
            cancellationToken);
        if (change is null || change.TenantId != request.ReviewerTenantId)
            return Result.NotFound("Remote-location change request not found.");
        if (!string.Equals(change.Status, "pending", StringComparison.Ordinal) ||
            change.Version != request.ExpectedVersion)
        {
            return Result.Conflict(
                "Remote-location change request was already reviewed or changed.");
        }
        if (change.NewProfileId is null)
        {
            return Result.Conflict(
                "Remote-location candidate profile is unavailable.");
        }

        var candidate = await _repository.GetRemoteProfileAsync(
            change.NewProfileId.Value,
            cancellationToken);
        if (candidate is null ||
            candidate.TenantId != request.ReviewerTenantId ||
            candidate.EmployeeId != change.EmployeeId)
        {
            return Result.Conflict(
                "Remote-location candidate profile is unavailable.");
        }

        var now = _clock.UtcNow;
        if (change.CurrentProfileId.HasValue)
        {
            var current = await _repository.GetRemoteProfileAsync(
                change.CurrentProfileId.Value,
                cancellationToken);
            if (current is not null &&
                current.TenantId == request.ReviewerTenantId &&
                current.EmployeeId == change.EmployeeId)
            {
                current.Status = "archived";
                current.ArchivedAt = now;
            }
        }

        candidate.Status = "active";
        candidate.ApprovedById = request.ReviewerUserId;
        candidate.ArchivedAt = null;
        change.Status = "approved";
        change.ReviewedById = request.ReviewerUserId;
        change.ReviewedAt = now;
        change.ReviewComment = string.IsNullOrWhiteSpace(request.ReviewComment)
            ? null
            : request.ReviewComment.Trim();
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

