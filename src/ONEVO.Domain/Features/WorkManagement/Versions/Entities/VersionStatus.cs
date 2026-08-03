namespace ONEVO.Domain.Features.WorkManagement.Versions.Entities;

/// <summary>Fixed global lookup, seeded 1=planned, 2=released, 3=archived. Same shape/seeding mechanism as ONEVO.Domain.Lookups (EmploymentType, Severity, etc.) — see LookupDataSeeder.</summary>
public class VersionStatus
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public static class VersionStatusIds
{
    public const int Planned = 1;
    public const int Released = 2;
    public const int Archived = 3;
}
