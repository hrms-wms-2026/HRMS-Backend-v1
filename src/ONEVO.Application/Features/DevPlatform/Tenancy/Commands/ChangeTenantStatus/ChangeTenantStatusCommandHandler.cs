using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;

public class ChangeTenantStatusCommandHandler : IRequestHandler<ChangeTenantStatusCommand, Result>
{
    private readonly ITenantRepository _tenants;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ChangeTenantStatusCommandHandler(
        ITenantRepository tenants,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangeTenantStatusCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure("Authentication required.", 403);

        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result.NotFound($"Tenant '{request.TenantId}' not found.");

        var target = MapAction(request.Action, tenant.Status);
        if (target is null)
            return Result.Failure(
                $"Cannot {request.Action.ToLowerInvariant()} a tenant in status '{tenant.Status.ToString().ToLowerInvariant()}'.",
                409);

        if (!IsValidTransition(tenant.Status, target.Value))
            return Result.Failure(
                $"Status transition from '{tenant.Status}' to '{target}' is not allowed.",
                409);

        var now = _clock.UtcNow;

        tenant.Status = target.Value;
        tenant.UpdatedAt = now;

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private static TenantStatus? MapAction(string action, TenantStatus current)
    {
        return action.ToLowerInvariant() switch
        {
            "suspend" => TenantStatus.Suspended,
            "unsuspend" => TenantStatus.Active,
            "activate" => TenantStatus.Active,
            "cancel" => TenantStatus.Cancelled,
            _ => null
        };
    }

    private static bool IsValidTransition(TenantStatus from, TenantStatus to) => (from, to) switch
    {
        (TenantStatus.Provisioning, TenantStatus.Cancelled) => true,
        (TenantStatus.Trial, TenantStatus.Active) => true,
        (TenantStatus.Trial, TenantStatus.Suspended) => true,
        (TenantStatus.Active, TenantStatus.Suspended) => true,
        (TenantStatus.Active, TenantStatus.Cancelled) => true,
        (TenantStatus.Suspended, TenantStatus.Active) => true,
        (TenantStatus.Suspended, TenantStatus.Cancelled) => true,
        _ => false
    };
}
