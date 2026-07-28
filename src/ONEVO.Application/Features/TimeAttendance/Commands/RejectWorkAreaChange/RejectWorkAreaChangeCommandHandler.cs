using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.RejectWorkAreaChange;

public sealed class RejectWorkAreaChangeCommandHandler
    : IRequestHandler<RejectWorkAreaChangeCommand, Result>
{
    private readonly ITimeAttendanceRepository _repository;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public RejectWorkAreaChangeCommandHandler(
        ITimeAttendanceRepository repository,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _repository = repository;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(
        RejectWorkAreaChangeCommand request,
        CancellationToken cancellationToken)
    {
        var change = await _repository.GetWorkAreaChangeAsync(
            request.RequestId,
            cancellationToken);
        if (change is null || change.TenantId != request.ReviewerTenantId)
            return Result.NotFound("Work-area change request not found.");
        if (!string.Equals(change.Status, "pending", StringComparison.Ordinal) ||
            change.Version != request.ExpectedVersion)
        {
            return Result.Conflict(
                "Work-area change request was already reviewed or changed.");
        }

        change.Status = "rejected";
        change.ReviewedById = request.ReviewerUserId;
        change.ReviewedAt = _clock.UtcNow;
        change.ReviewComment = string.IsNullOrWhiteSpace(request.ReviewComment)
            ? null
            : request.ReviewComment.Trim();
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

