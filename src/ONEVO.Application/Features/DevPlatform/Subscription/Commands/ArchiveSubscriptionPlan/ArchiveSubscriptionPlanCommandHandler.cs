using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.ArchiveSubscriptionPlan;

public sealed class ArchiveSubscriptionPlanCommandHandler : IRequestHandler<ArchiveSubscriptionPlanCommand, Result>
{
    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ArchiveSubscriptionPlanCommandHandler(
        ISubscriptionPlanRepository planRepo,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _planRepo = planRepo;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ArchiveSubscriptionPlanCommand request, CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result.NotFound($"Subscription plan '{request.PlanId}' not found.");

        plan.IsActive = false;
        plan.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
