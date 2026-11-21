using ONEVO.Domain.Common;

namespace ONEVO.Domain.Features.Monitoring.Settings.Entities;

/// <summary>
/// One row of monitoring switches for one scope (Improvements task 36). It replaces
/// <c>monitoring_feature_toggles</c>, <c>monitoring_policy_overrides</c> and
/// <c>employee_monitoring_overrides</c>, so a new switch is one column in one table.
/// <para>
/// "Not set at this level": on a base row (<see cref="MonitoringSettingScopes.LegalEntity"/> or the
/// retained <see cref="MonitoringSettingScopes.Tenant"/> fallback) every on/off switch has a value,
/// which a check constraint enforces. On an override row (work mode, role, position, department,
/// employee) a null switch means "inherit from the next level". The resolver order is
/// employee, work mode, role, position, department, legal entity, tenant, then off.
/// </para>
/// Unique on (tenant_id, scope_type, scope_id). For the tenant scope, scope_id is the tenant id.
/// </summary>
public class MonitoringSetting : ITenantOwnedEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>One of <see cref="MonitoringSettingScopes.All"/>.</summary>
    public string ScopeType { get; set; } = string.Empty;

    /// <summary>The row the scope names: legal entity, department, position, role, work mode, employee, or the tenant itself.</summary>
    public Guid ScopeId { get; set; }

    public bool? ActivityMonitoring { get; set; }
    public bool? ApplicationTracking { get; set; }
    public bool? DocumentTracking { get; set; }
    public bool? CommunicationTracking { get; set; }
    public bool? ScreenshotCapture { get; set; }
    public bool? AutoScreenshotCapture { get; set; }
    public bool? MeetingDetection { get; set; }
    public bool? DeviceTracking { get; set; }
    public bool? WorkLocationVerification { get; set; }
    public bool? IdentityVerification { get; set; }
    public bool? Biometric { get; set; }

    /// <summary>
    /// Clock-in camera photo. Read only from an employee row today (falling back to the work mode's
    /// own setting); every scope has the column so all scopes share one shape.
    /// </summary>
    public bool? PhotoRequired { get; set; }

    /// <summary>Minutes of inactivity before the tray app's activity check. Null = inherit (base row: the default).</summary>
    public int? IdleThresholdMinutes { get; set; }

    /// <summary>Maximum clock-in distance from the work location in meters. Null = inherit (base row: no limit).</summary>
    public int? AllowedRadiusMeters { get; set; }

    /// <summary>Why an override was set. Null on base rows.</summary>
    public string? OverrideReason { get; set; }

    /// <summary>The user who set an override. Null on base rows.</summary>
    public Guid? SetById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsBase => MonitoringSettingScopes.Base.Contains(ScopeType);
}

public static class MonitoringSettingScopes
{
    public const string Tenant = "tenant";
    public const string LegalEntity = "legal_entity";
    public const string Department = "department";
    public const string Position = "position";
    public const string Role = "role";
    public const string WorkMode = "work_mode";
    public const string Employee = "employee";

    public static IReadOnlyList<string> All { get; } =
        [Tenant, LegalEntity, Department, Position, Role, WorkMode, Employee];

    /// <summary>Company defaults: every switch has a value.</summary>
    public static IReadOnlyList<string> Base { get; } = [Tenant, LegalEntity];

    /// <summary>The overrides the monitoring settings screen manages.</summary>
    public static IReadOnlyList<string> Policy { get; } = [Department, Position, Role, WorkMode];
}
