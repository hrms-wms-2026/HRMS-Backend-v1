namespace ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

public class ConfigurationTemplate
{
    public const string TypeConfiguration = "configuration";
    public const string TypePositionTemplate = "position_template";
    public const string TypeTimeOffPolicy = "time_off_policy";
    public const string TypeMonitoringPolicy = "monitoring_policy";
    public const string TypeAppAllowlist = "app_allowlist";
    public const string TypeOnboarding = "onboarding";
    public const string TypeDataImportMapping = "data_import_mapping";

    public static readonly IReadOnlyList<string> AllTypes = new[]
    {
        TypeConfiguration,
        TypePositionTemplate,
        TypeTimeOffPolicy,
        TypeMonitoringPolicy,
        TypeAppAllowlist,
        TypeOnboarding,
        TypeDataImportMapping,
    };

    public Guid Id { get; set; }
    public string TemplateKey { get; set; } = string.Empty;
    public string TemplateType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public string ModuleKeysJson { get; set; } = "[]";
    public string? IndustryProfileTag { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
