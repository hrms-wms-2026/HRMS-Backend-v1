namespace ONEVO.Domain.Lookups;

/// <summary>Fixed global lookup, seeded by LookupDataSeeder (Id=1 "active", Id=4 "terminated")
/// and backfilled for existing databases by migration 20260817120000 (Id=5 "offboarding",
/// Id=6 "resigned" — LookupDataSeeder's per-table AnyAsync guard means array-only additions
/// never reach an already-seeded database, so the migration's InsertData is the real delivery
/// mechanism for those two rows). Same shape/seeding mechanism as VersionStatusIds
/// (src/ONEVO.Domain/Features/WorkManagement/Versions/Entities/VersionStatus.cs).</summary>
public static class EmploymentStatusIds
{
    public const int Active = 1;
    public const int OnLeave = 2;
    public const int Suspended = 3;
    public const int Terminated = 4;
    public const int Offboarding = 5;
    public const int Resigned = 6;
}
