using System.Text.Json;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Subscription.Helpers;
using ONEVO.Application.Features.DevPlatform.Subscription.Mappers;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Commands.CreateSubscriptionPlan;

public sealed class CreateSubscriptionPlanCommandHandler
    : IRequestHandler<CreateSubscriptionPlanCommand, Result<SubscriptionPlanDetailDto>>
{
    private static readonly ModulePricingCalculator Calculator = new();

    private readonly ISubscriptionPlanRepository _planRepo;
    private readonly IModuleCatalogService _catalogService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _clock;

    public CreateSubscriptionPlanCommandHandler(
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
        CreateSubscriptionPlanCommand request,
        CancellationToken ct)
    {
        if (await _planRepo.ExistsByCodeAsync(request.Code, ct))
            return Result<SubscriptionPlanDetailDto>.Conflict(
                $"A subscription plan with code '{request.Code}' already exists.");

        var catalogItems = await _catalogService.GetByCatalogKeysAsync(request.ModuleKeys, ct);

        var missingKeys = request.ModuleKeys
            .Except(catalogItems.Select(m => m.ModuleKey), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missingKeys.Count > 0)
            return Result<SubscriptionPlanDetailDto>.Failure(
                $"Unknown module keys: {string.Join(", ", missingKeys)}.");

        ModulePricingResult pricing;
        try
        {
            pricing = Calculator.Calculate(catalogItems, request.ModuleKeys, request.CompanySizeRange);
        }
        catch (InvalidOperationException ex)
        {
            return Result<SubscriptionPlanDetailDto>.Failure(ex.Message);
        }

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Code = request.Code,
            Tier = request.Tier,
            CompanySizeRange = request.CompanySizeRange,
            PricingUnit = catalogItems.First().PricingUnit,
            IncludedModulesJson = JsonSerializer.Serialize(request.ModuleKeys),
            CalculatedMonthlyPrice = pricing.CalculatedMonthlyPrice,
            CalculatedAnnualPrice = pricing.CalculatedAnnualPrice,
            OverrideMonthlyPrice = request.OverrideMonthlyPrice,
            OverrideAnnualPrice = request.OverrideAnnualPrice,
            AiTokenLimitPerMonth = request.AiTokenLimitPerMonth,
            Currency = request.Currency,
            TrialPeriodDays = request.TrialPeriodDays,
            UnpaidGracePeriodDays = request.UnpaidGracePeriodDays,
            CreatedAt = _clock.UtcNow
        };

        await _planRepo.AddAsync(plan, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SubscriptionPlanDetailDto>.Success(SubscriptionPlanMapper.ToDetailDto(plan));
    }
}
