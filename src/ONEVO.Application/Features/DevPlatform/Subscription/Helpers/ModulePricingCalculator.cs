using System.Text.Json;
using ONEVO.Domain.Features.SharedPlatform.Entities;

namespace ONEVO.Application.Features.DevPlatform.Subscription.Helpers;

public sealed class ModulePricingCalculator
{
    public ModulePricingResult Calculate(
        IReadOnlyList<ModuleCatalogItem> catalog,
        IReadOnlyList<string> selectedModuleKeys,
        string companySizeRange)
    {
        if (selectedModuleKeys.Count == 0)
            throw new InvalidOperationException("At least one module must be selected.");

        var selected = selectedModuleKeys
            .Select(key => catalog.FirstOrDefault(m => m.ModuleKey == key)
                ?? throw new InvalidOperationException($"Module '{key}' not found in catalog."))
            .ToList();

        var inactiveModule = selected.FirstOrDefault(m => !m.IsActive);
        if (inactiveModule is not null)
            throw new InvalidOperationException(
                $"Module '{inactiveModule.ModuleKey}' is not active and cannot be included in a plan.");

        var pricingUnits = selected.Select(m => m.PricingUnit).Distinct().ToList();
        if (pricingUnits.Count > 1)
            throw new InvalidOperationException(
                $"Selected modules have mixed pricing units ({string.Join(", ", pricingUnits)}). " +
                "All modules in a reusable plan must share the same pricing unit.");

        var (rangeMin, rangeMax) = ParseSizeRange(companySizeRange);

        decimal totalMonthly = 0m;
        decimal totalAnnual = 0m;

        foreach (var module in selected)
        {
            var brackets = JsonSerializer.Deserialize<List<PriceBracket>>(
                module.PricingReference,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })
                ?? throw new InvalidOperationException(
                    $"Module '{module.ModuleKey}' has invalid pricing_reference JSON.");

            PriceBracket? bracket = null;
            if (rangeMax == -1)
                bracket = brackets.FirstOrDefault(b => b.MaxEmployees == -1);
            else
                bracket = brackets.FirstOrDefault(b =>
                    b.MinEmployees <= rangeMin && b.MaxEmployees >= rangeMax);

            if (bracket is null)
                throw new InvalidOperationException(
                    $"No price bracket found for module '{module.ModuleKey}' " +
                    $"and company size range '{companySizeRange}'.");

            totalMonthly += bracket.MonthlyPrice;
            totalAnnual += bracket.AnnualPrice;
        }

        return new ModulePricingResult(
            CalculatedMonthlyPrice: totalMonthly,
            CalculatedAnnualPrice: totalAnnual,
            SelectedModuleKeys: selectedModuleKeys);
    }

    private static (int Min, int Max) ParseSizeRange(string range)
    {
        if (range.EndsWith('+'))
        {
            var minStr = range.TrimEnd('+');
            if (!int.TryParse(minStr, out var min))
                throw new InvalidOperationException($"Invalid company size range: '{range}'");
            return (min, -1);
        }

        var parts = range.Split('-');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var lo) &&
            int.TryParse(parts[1], out var hi))
            return (lo, hi);

        throw new InvalidOperationException($"Invalid company size range: '{range}'");
    }

    private sealed record PriceBracket(
        int MinEmployees,
        int MaxEmployees,
        decimal MonthlyPrice,
        decimal AnnualPrice);
}

public sealed record ModulePricingResult(
    decimal CalculatedMonthlyPrice,
    decimal CalculatedAnnualPrice,
    IReadOnlyList<string> SelectedModuleKeys);
