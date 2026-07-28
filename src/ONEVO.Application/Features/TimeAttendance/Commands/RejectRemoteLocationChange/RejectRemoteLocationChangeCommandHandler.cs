using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.IdentityVerification.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RejectRemoteLocationChange;

public sealed class RejectRemoteLocationChangeCommandHandler
    : IRequestHandler<RejectRemoteLocationChangeCommand, Result>
{
    private readonly IVerificationRepository _repository;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RejectRemoteLocationChangeCommandHandler(
        IVerificationRepository repository,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _repository = repository;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(
        RejectRemoteLocationChangeCommand request,
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

        var now = _clock.UtcNow;
        if (change.NewProfileId.HasValue)
        {
            var candidate = await _repository.GetRemoteProfileAsync(
                change.NewProfileId.Value,
                cancellationToken);
            if (candidate is not null &&
                candidate.TenantId == request.ReviewerTenantId &&
                candidate.EmployeeId == change.EmployeeId)
            {
                candidate.Status = "rejected";
                candidate.ArchivedAt = now;
            }
        }

        change.Status = "rejected";
        change.ReviewedById = request.ReviewerUserId;
        change.ReviewedAt = now;
        change.ReviewComment = string.IsNullOrWhiteSpace(request.ReviewComment)
            ? null
            : request.ReviewComment.Trim();
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

