using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Provisioning;
using ONEVO.Application.Features.DevPlatform.Tenancy.Provisioning;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.CreateOneTimeCharge;

public sealed class CreateOneTimeChargeCommandHandler
    : IRequestHandler<CreateOneTimeChargeCommand, Result<TenantOneTimeChargeDto>>
{
    private readonly ITenantOneTimeChargeRepository _repo;
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateOneTimeChargeCommandHandler(
        ITenantOneTimeChargeRepository repo,
        ITenantRepository tenants,
        IUnitOfWork unitOfWork,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _repo = repo;
        _tenants = tenants;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<TenantOneTimeChargeDto>> Handle(
        CreateOneTimeChargeCommand request,
        CancellationToken ct)
    {
        var tenant = await _tenants.GetByIdAsync(request.TenantId, ct);
        if (tenant is null)
            return Result<TenantOneTimeChargeDto>.NotFound($"Tenant '{request.TenantId}' not found.");

        if (!TenantSetupOptionKeys.All.Contains(request.SetupOptionKey))
            return Result<TenantOneTimeChargeDto>.Failure(
                $"Unknown setup option key '{request.SetupOptionKey}'.", 400);

        var hasActive = await _repo.HasActiveChargeAsync(request.TenantId, request.SetupOptionKey, ct);
        if (hasActive)
            return Result<TenantOneTimeChargeDto>.Conflict(
                $"A non-void one-time charge for '{request.SetupOptionKey}' already exists for this tenant.");

        var now = _clock.UtcNow;
        var charge = new TenantOneTimeCharge
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            SetupOptionKey = request.SetupOptionKey,
            Description = request.Description,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = "draft",
            ChargedOnce = true,
            CreatedAt = now,
            CreatedById = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty
        };

        await _repo.AddAsync(charge, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<TenantOneTimeChargeDto>.Success(new TenantOneTimeChargeDto(
            charge.Id,
            charge.TenantId,
            charge.SetupOptionKey,
            charge.Description,
            charge.Amount,
            charge.Currency,
            charge.Status,
            charge.ChargedOnce,
            charge.CreatedAt));
    }
}
