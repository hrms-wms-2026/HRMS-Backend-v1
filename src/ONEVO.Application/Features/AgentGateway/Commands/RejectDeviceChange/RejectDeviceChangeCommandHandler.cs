using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Features.AgentGateway.RepositoryInterfaces;

namespace ONEVO.Application.Features.AgentGateway.Commands.RejectDeviceChange;

public sealed class RejectDeviceChangeCommandHandler
    : IRequestHandler<RejectDeviceChangeCommand, Result>
{
    private readonly IAgentGatewayRepository _repo;
    private readonly IUnitOfWork _uow;

    public RejectDeviceChangeCommandHandler(
        IAgentGatewayRepository repo,
        IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<Result> Handle(
        RejectDeviceChangeCommand command,
        CancellationToken cancellationToken)
    {
        var reviewComment = command.ReviewComment?.Trim();
        if (reviewComment?.Length > 500)
            return Result.Failure("Review comment cannot exceed 500 characters.");

        var request = await _repo.GetDeviceChangeRequestByIdAsync(
            command.RequestId, cancellationToken);
        if (request is null)
            return Result.NotFound("Device change request not found.");
        if (!string.Equals(request.Status, "pending", StringComparison.Ordinal))
            return Result.Conflict("Device change request is no longer pending.");

        var now = DateTimeOffset.UtcNow;
        request.Status = "rejected";
        request.ReviewedById = command.ReviewedById;
        request.ReviewedAt = now;
        request.ReviewComment = reviewComment;
        request.UpdatedAt = now;

        await _uow.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
