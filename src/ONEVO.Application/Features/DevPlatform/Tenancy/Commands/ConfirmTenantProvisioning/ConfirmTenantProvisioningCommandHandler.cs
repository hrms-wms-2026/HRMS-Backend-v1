using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Queries.GetProvisioningSummary;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ConfirmTenantProvisioning;

public class ConfirmTenantProvisioningCommandHandler
    : IRequestHandler<ConfirmTenantProvisioningCommand, Result<ProvisioningSummaryDto>>
{
    private const string ActivationReason = "provisioning_confirmed";

    private readonly ITenantRepository _tenants;
    private readonly ITenantStatusHistoryRepository _statusHistories;
    private readonly IMediator _mediator;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public ConfirmTenantProvisioningCommandHandler(
        ITenantRepository tenants,
        ITenantStatusHistoryRepository statusHistories,
        IMediator mediator,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _tenants = tenants;
        _statusHistories = statusHistories;
        _mediator = mediator;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ProvisioningSummaryDto>> Handle(
        ConfirmTenantProvisioningCommand request,
        CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result<ProvisioningSummaryDto>.Forbidden("Authentication required.");

        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<ProvisioningSummaryDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        if (tenant.Status != TenantStatus.Provisioning)
            return Result<ProvisioningSummaryDto>.Conflict(
                $"Tenant is in status '{tenant.Status.ToString().ToLowerInvariant()}'; only provisioning tenants can be confirmed.");

        var summaryResult = await _mediator.Send(
            new GetProvisioningSummaryQuery(tenant.Id),
            ct);

        if (!summaryResult.IsSuccess || summaryResult.Value is null)
            return Result<ProvisioningSummaryDto>.Failure(
                summaryResult.Error ?? "Failed to compute provisioning summary.",
                summaryResult.StatusCode ?? 500);

        var summary = summaryResult.Value;

        if (!summary.CanActivate)
            return Result<ProvisioningSummaryDto>.Failure(
                "Tenant is not ready for activation. Resolve the blocking errors and retry.",
                statusCode: 422);

        var now = _clock.UtcNow;
        var previousStatus = tenant.Status;

        tenant.Status = TenantStatus.Trial;
        tenant.UpdatedAt = now;

        var history = new TenantStatusHistory
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            FromStatus = previousStatus,
            ToStatus = TenantStatus.Trial,
            Reason = ActivationReason,
            ChangedById = _currentUser.UserId,
            ChangedAt = now
        };
        await _statusHistories.AddAsync(history, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProvisioningSummaryDto>.Success(summary with
        {
            Status = TenantStatus.Trial.ToString().ToLowerInvariant(),
            CanActivate = true
        });
    }
}
