using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.DevPlatform.ConfigurationTemplates.Entities;

public class TenantConfigurationTemplateApplication : ITenantOwnedEntity
{
    public const string StatusApplied = "applied";

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConfigurationTemplateId { get; set; }
    public string TemplateType { get; set; } = string.Empty;
    public int AppliedVersion { get; set; }
    public string AppliedPayloadJson { get; set; } = "{}";
    public string? CustomPayloadJson { get; set; }
    public string? WarningsJson { get; set; }
    public string Status { get; set; } = StatusApplied;
    public Guid AppliedById { get; set; }
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;
}
