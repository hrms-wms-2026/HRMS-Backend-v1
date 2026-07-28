using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.TimeAttendance.RepositoryInterfaces;

namespace ONEVO.Application.Features.TimeAttendance.Commands.ApproveWorkAreaChange;

public sealed class ApproveWorkAreaChangeCommandHandler
    : IRequestHandler<ApproveWorkAreaChangeCommand, Result>
{
    private readonly ITimeAttendanceRepository _repository;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;

    public ApproveWorkAreaChangeCommandHandler(
        ITimeAttendanceRepository repository,
        IDateTimeProvider clock,
        IUnitOfWork uow)
    {
        _repository = repository;
        _clock = clock;
        _uow = uow;
    }

    public async Task<Result> Handle(
        ApproveWorkAreaChangeCommand request,
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

        change.Status = "approved";
        change.ReviewedById = request.ReviewerUserId;
        change.ReviewedAt = _clock.UtcNow;
        change.ReviewComment = NormalizeComment(request.ReviewComment);
        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static string? NormalizeComment(string? comment) =>
        string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();
}

