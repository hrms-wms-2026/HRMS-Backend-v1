using ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

namespace ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Helpers;

internal static class ConfigurationTemplateModuleRequirement
{
    private static readonly IReadOnlyDictionary<string, string?> RequiredModuleByType =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConfigurationTemplate.TypeConfiguration] = null,
            [ConfigurationTemplate.TypePositionTemplate] = "core_hr",
            [ConfigurationTemplate.TypeTimeOffPolicy] = "time_off",
            [ConfigurationTemplate.TypeMonitoringPolicy] = "monitoring",
            [ConfigurationTemplate.TypeAppAllowlist] = "monitoring",
            [ConfigurationTemplate.TypeOnboarding] = "core_hr",
            [ConfigurationTemplate.TypeDataImportMapping] = "core_hr",
        };

    internal static string? RequiredModuleKeyFor(string templateType) =>
        RequiredModuleByType.TryGetValue(templateType, out var moduleKey) ? moduleKey : null;
}
