using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Application.Features.DevPlatform.Subscription.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.UpdateSubscriptionPlan;

public sealed class UpdateSubscriptionPlanCommandHandler
    : IRequestHandler<UpdateSubscriptionPlanCommand, Result<SubscriptionPlanDetailDto>>
{
    private static readonly ModulePricingCalculator Calculator = new();

    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly IModuleCatalogService _catalogService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public UpdateSubscriptionPlanCommandHandler(
        ISubscriptionPlanRepository planRepo,
        IModuleCatalogService catalogService,
        IUnitOfWork unitOfWork,
        IDateTimeProvider clock)
    {
        _planRepo = planRepo;
        _catalogService = catalogService;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SubscriptionPlanDetailDto>> Handle(
        UpdateSubscriptionPlanCommand request,
        CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(request.PlanId, ct);
        if (plan is null)
            return Result<SubscriptionPlanDetailDto>.NotFound(
                $"Subscription plan '{request.PlanId}' not found.");

        if (request.Name is not null) plan.Name = request.Name;
        if (request.Tier is not null) plan.Tier = request.Tier;
        if (request.Currency is not null) plan.Currency = request.Currency;
        if (request.OverrideMonthlyPrice is not null) plan.OverrideMonthlyPrice = request.OverrideMonthlyPrice;
        if (request.OverrideAnnualPrice is not null) plan.OverrideAnnualPrice = request.OverrideAnnualPrice;
        if (request.AiTokenLimitPerMonth is not null) plan.AiTokenLimitPerMonth = request.AiTokenLimitPerMonth;
        if (request.TrialPeriodDays is not null) plan.TrialPeriodDays = request.TrialPeriodDays.Value;
        if (request.UnpaidGracePeriodDays is not null) plan.UnpaidGracePeriodDays = request.UnpaidGracePeriodDays.Value;
        if (request.ModuleKeys is not null)
            plan.IncludedModulesJson = JsonSerializer.Serialize(request.ModuleKeys);
        if (request.CompanySizeRange is not null)
            plan.CompanySizeRange = request.CompanySizeRange;

        bool needsRecalculation = request.ModuleKeys is not null || request.CompanySizeRange is not null;
        if (needsRecalculation)
        {
            var moduleKeys = plan.GetIncludedModules();
            var catalogItems = await _catalogService.GetByCatalogKeysAsync(moduleKeys, ct);

            var missingKeys = moduleKeys
                .Except(catalogItems.Select(m => m.ModuleKey), StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingKeys.Count > 0)
                return Result<SubscriptionPlanDetailDto>.Failure(
                    $"Unknown module keys: {string.Join(", ", missingKeys)}.");

            ModulePricingResult pricing;
            try
            {
                pricing = Calculator.Calculate(catalogItems, moduleKeys, plan.CompanySizeRange);
            }
            catch (InvalidOperationException ex)
            {
                return Result<SubscriptionPlanDetailDto>.Failure(ex.Message);
            }

            plan.PricingUnit = catalogItems.First().PricingUnit;
            plan.CalculatedMonthlyPrice = pricing.CalculatedMonthlyPrice;
            plan.CalculatedAnnualPrice = pricing.CalculatedAnnualPrice;
        }

        plan.UpdatedAt = _clock.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SubscriptionPlanDetailDto>.Success(SubscriptionPlanMapper.ToDetailDto(plan));
    }
}
