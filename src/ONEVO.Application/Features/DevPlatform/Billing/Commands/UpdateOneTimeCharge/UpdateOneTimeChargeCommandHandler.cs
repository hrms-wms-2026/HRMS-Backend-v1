using MediatR;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.Billing.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Billing.Commands.UpdateOneTimeCharge;

public sealed class UpdateOneTimeChargeCommandHandler
    : IRequestHandler<UpdateOneTimeChargeCommand, Result<TenantOneTimeChargeDto>>
{
    private readonly ITenantOneTimeChargeRepository _repo;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateOneTimeChargeCommandHandler(
        ITenantOneTimeChargeRepository repo,
        IUnitOfWork unitOfWork)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<TenantOneTimeChargeDto>> Handle(
        UpdateOneTimeChargeCommand request,
        CancellationToken ct)
    {
        var charge = await _repo.GetByIdAsync(request.ChargeId, ct);
        if (charge is null)
            return Result<TenantOneTimeChargeDto>.NotFound(
                $"One-time charge '{request.ChargeId}' not found.");

        if (charge.TenantId != request.TenantId)
            return Result<TenantOneTimeChargeDto>.NotFound(
                $"One-time charge '{request.ChargeId}' not found for this tenant.");

        if (charge.Status is "paid" or "void")
            return Result<TenantOneTimeChargeDto>.Failure(
                $"Cannot edit a charge with status '{charge.Status}'.", 400);

        if (request.Amount.HasValue)
            charge.Amount = request.Amount.Value;

        if (request.Status is not null)
            charge.Status = request.Status;

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
