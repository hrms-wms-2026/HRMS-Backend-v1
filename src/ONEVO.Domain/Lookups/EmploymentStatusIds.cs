namespace ONEVO.Domain.Lookups;

/// <summary>Fixed global lookup, seeded by LookupDataSeeder (Id=1 "active", Id=4 "terminated").
/// Same shape/seeding mechanism as VersionStatusIds (src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs).</summary>
public static class EmploymentStatusIds
{
    public const int Active = 1;
}
