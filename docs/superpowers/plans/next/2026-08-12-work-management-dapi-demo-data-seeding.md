# Work Management Dapi Demo Data Seeding — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the `dapi` dev tenant (owner `dapiyshanth1908@gmail.com`) a realistic, hand-designed Work Management dataset: 22 new demo employees organized into 6 teams, 5 real Projects (E-pos_System, Event management ticketing, Onexso, Watercraft, The Hardware integration portal), and a 5-layer Objective/milestone tree per project with correct owners and members — all inserted by a new idempotent dev-only seeder, without disturbing the existing `DevSmokeTestTenantSeeder` fixed-count tests or cluttering the tenant with the generic `WorkManagementSampleDataSeeder`'s auto-generated "SMK…" sample projects.

**Architecture:** Two new files (`WorkManagementDapiDemoData.cs` — pure declarative tree/roster data; `WorkManagementDapiDemoSeeder.cs` — an `IHostedService` that walks that data and upserts `User`/`Employee`/`Role`/`RolePermission`/`ProjectCategory`/`Project`/`Objective`/`ProjectMember` rows via deterministic (MD5-derived) fixed `Guid`s so re-seeding on every dev boot is a no-op), registered in `DependencyInjection.cs` right after `DevSmokeTestTenantSeeder` and before `ProjectsAccessBootstrapSeeder`/`WorkManagementSampleDataSeeder`. A one-line guard is added to the existing `WorkManagementSampleDataSeeder` so it skips the `dapi` tenant entirely, since the new seeder becomes the sole source of dapi's Work Management data.

**Tech Stack:** ASP.NET Core `IHostedService` seeders, EF Core against `ApplicationDbContext`, xUnit + `SqliteTestApplicationDbContext` (matching `DevSmokeTestTenantSeederTests`' existing harness) for verification.

## Global Constraints

- Dev/Test-only: every new seeder must reproduce the existing `!_environment.IsDevelopment() && !_environment.IsEnvironment("Test")` early-return guard (see `DevSmokeTestTenantSeeder.cs:137-140`, `WorkManagementSampleDataSeeder.cs:53-56`).
- Must not modify `DevSmokeTestTenantSeeder.cs` or its fixed Guids/consts — `DevSmokeTestTenantSeederTests.cs` asserts exact tenant/user/employee/subscription counts and must keep passing unchanged.
- Every insert must be idempotent (upsert-by-fixed-Id or check-exists-then-skip) because the hosted service re-runs on every dev boot — no unique-constraint violations on the second run.
- Scope is Work Management only: the new Role must be granted only `Permission` rows where `Module == "work_management"` (19 codes, see Task 2) — never HR/payroll/admin permissions.
- `ObjectiveParentConstraintChecker.Conflicts` (`src/ONEVO.Application/Features/WorkManagement/Objectives/Helpers/ObjectiveParentConstraintChecker.cs`) requires every child Objective's `[StartDate, EndDate]` to fall inside its parent's range (inclusive) and `AllocatedHours` to not exceed the parent's `AllocatedHours` — the tree-building algorithm in Task 1 must satisfy this by construction for every one of the ~66 nodes.
- All FKs on WorkManagement tables use `DeleteBehavior.Restrict` — this plan only inserts, never deletes, so this is a non-issue, but do not add any cleanup/reset logic that deletes existing rows.

---

## Background — what already exists (read this before touching any file)

- **Tenant:** `dapi` (`DapiTenantId = 6b0874ab-71db-401f-859f-bdd50c1317fb`), owner user `Dapi Owner` (`DapiOwnerUserId = cd49a0c2-e978-4055-b8be-7d46a3727e94`, email `dapiyshanth1908@gmail.com`, employee number `DAPI-0001`), legal entity `Dapi Technologies` (`DapiLegalEntityId = 57fecfe8-1c1e-4a82-be4b-2c8451436420`) — all seeded by `src/ONEVO.Infrastructure/Persistence/Seeders/DevSmokeTestTenantSeeder.cs`. The Dapi Owner's role ("Tenant Owner") already has **every** seeded `Permission` except the `"*"` bypass row (`ResolveRolePermissionsAsync`, `DevSmokeTestTenantSeeder.cs:593-626`), so Dabi needs no new permission grants.
- **Seeder boot order** (`src/ONEVO.Infrastructure/DependencyInjection.cs:401-409`, hosted services run `StartAsync` in registration order):
  ```text
  PermissionSeeder → RoleTemplateSeeder → LookupDataSeeder → PlatformAccessSeeder →
  ModuleCatalogSeeder → DevSmokeTestTenantSeeder → PlatformOAuthProviderMetadataSeeder →
  ProjectsAccessBootstrapSeeder → WorkManagementSampleDataSeeder
  ```
  `ProjectsAccessBootstrapSeeder` grants permission code `projects:access` to **every** `Role` in the dapi tenant by live-querying `db.Roles.Where(r => r.TenantId == user.TenantId)` at run time (`ProjectsAccessBootstrapSeeder.cs:92-95`) — no cache, no `CreatedAt` cutoff — so any Role our new seeder creates gets this grant automatically on the same boot, **as long as our seeder's `SaveChangesAsync` commits before `ProjectsAccessBootstrapSeeder` runs its query**. This is why the new seeder is registered before it.
- **Entity model** (all under `src/ONEVO.Domain/Features/WorkManagement/…/Entities/`, all inherit `BaseEntity` → `Id`, `TenantId`, `CreatedAt`, `UpdatedAt`, `CreatedById`, `IsDeleted`):
  - `Objective` (ns `ONEVO.Domain.Features.WorkManagement.Objectives.Entities`): `ProjectId`, `ParentObjectiveId` (nullable, self-referencing), `IsDefault`, `Title`, `Description`, `OwnerId`, `ReportingManagerId` (nullable), `IsActive`, `StartDate`/`EndDate` (`DateOnly`), `Progress`, `ActualHours` (nullable), `AllocatedHours`, `CompletedHours`, `IsAchieved`, `AchievedAt`. **There is no separate Milestone entity — a "milestone" is an `Objective` with `IsDefault = false`.** Only one `IsDefault = true` row per `(TenantId, ProjectId)` is allowed (partial unique index).
  - `Project` (ns `ONEVO.Domain.Features.WorkManagement.Projects.Entities`): `OwningLegalEntityId`, `CategoryId`, `Name`, `Identifier` (unique per `(TenantId, Identifier)`, ≤20 chars), `NextTaskNumber` (defaults to 1), `Description`, `LeadId`, `StartDate`/`TargetDate`, `AllocatedHours`/`CompletedHours`, `IsActive`, `IsAchieved`/`AchievedAt`.
  - `ProjectMember` (ns `ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities`): `ProjectId`, `ObjectiveId`, `UserId`, `EmployeeId`, `MembershipSource` (use `ProjectMembershipSources.System`), `IsActive`, `JoinedAt`. **This table is both project membership and Objective membership** — unique on `(TenantId, ProjectId, ObjectiveId, UserId)`.
  - `ProjectCategory` (ns `ONEVO.Domain.Features.WorkManagement.Projects.Entities`): `Name` (unique per `(TenantId, Name)`), `IsActive`.
  - `User` (ns `ONEVO.Domain.Features.InfrastructureModule.Entities`): `Email`, `PasswordHash`, `FirstName`, `LastName`, `IsActive`, `EmailVerified`, `MustChangePassword`, `PasswordSetByAdmin`.
  - `Employee` (ns `ONEVO.Domain.Features.CoreHr.Entities`): `UserId` (unique), `EmployeeNumber` (unique per tenant), `FirstName`, `LastName`, `Email`, `LegalEntityId` (nullable), `EmploymentTypeId`/`EmploymentStatusId`/`WorkModeId` (default `1`/`1`/`1`, pre-seeded by `LookupDataSeeder`), `HireDate`.
  - `Role`/`Permission`/`RolePermission`/`UserRole` (ns `ONEVO.Domain.Features.Auth.Entities`, same `using` as `DevSmokeTestTenantSeeder.cs`): `Role { Id, TenantId, Name, Description, IsSystem, CreatedAt, CreatedById }`, `RolePermission { TenantId, RoleId, PermissionId }`, `UserRole { TenantId, UserId, RoleId, AssignedAt, AssignedBy }`.
  - `ApplicationDbContext` DbSets used: `Tenants`, `LegalEntities`, `Users`, `Employees`, `Roles`, `Permissions`, `RolePermissions`, `UserRoles`, `ProjectCategories`, `Projects`, `Objectives`, `ProjectMembers`.
- **`ObjectiveParentConstraintChecker.Conflicts`** (full file already read): `datesOutOfRange = startDate < parent.StartDate || endDate > parent.EndDate; hoursExceeded = allocatedHours > parent.AllocatedHours;` — inclusive bounds, hours checked against parent's **total**, not remaining headroom.
- **`work_management`-tagged permission codes** (from `PermissionSeeder.cs:233-260`, exact list the new Role must be granted): `tasks:read`, `tasks:write`, `tasks:approve`, `tasks:delete`, `time:read`, `time:write`, `time:approve`, `projects:read`, `projects:access`, `okr:read`, `okr:write`, `wiki:read`, `wiki:write`, `sprints:read`, `sprints:manage`, `workspaces:read`, `workspaces:create`, `workspaces:manage`, `resources:read`, `resources:manage`, `roadmaps:read`, `roadmaps:write` — 21 codes total (all rows where `Module == "work_management"`).
- **`WorkManagementSampleDataSeeder`** (full file already read, `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs`): loops every `TenantStatus.Active` tenant (line 65-67), and for dapi this will now also iterate our 22 new active Users-with-Employee rows, creating 2 "SMK…" sample projects each (44 extra junk projects) unless guarded. Task 5 adds a one-line skip for `tenant.Slug == "dapi"`.

---

### Task 1: Demo roster + objective-tree data model

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoData.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoDataTests.cs`

**Interfaces:**
- Produces: `internal sealed record DemoPerson(string Key, string FirstName, string LastName, string Email, string EmployeeNumber, DateOnly HireDate)`; `internal sealed record DemoObjectiveNode(string Title, string OwnerKey, string[] ExtraMemberKeys, DemoObjectiveNode[] Children)`; `internal sealed record DemoProjectTree(string ProjectKey, string ProjectName, string Identifier, string CategoryName, DateOnly StartDate, DateOnly TargetDate, decimal AllocatedHours, DemoObjectiveNode Root)`; static class `WorkManagementDapiDemoData` exposing `IReadOnlyList<DemoPerson> Persons`, `IReadOnlyDictionary<string, DemoPerson> PersonsByKey`, `IReadOnlyList<string> ProjectCategoryNames`, `IReadOnlyList<DemoProjectTree> ProjectTrees`. Consumed by Task 2/3's seeder.

- [ ] **Step 1: Write the data file**

```csharp
namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only: hand-designed Work Management demo dataset for the "dapi" smoke tenant
/// (owner dapiyshanth1908@gmail.com). Pure data - no DB access here. Consumed by
/// WorkManagementDapiDemoSeeder. Every DemoObjectiveNode.Children entry must satisfy
/// ObjectiveParentConstraintChecker.Conflicts by construction once dates/hours are computed by
/// the seeder's tree walk (see WorkManagementDapiDemoSeeder.ComputeChildDates/ComputeChildHours).
/// </summary>
internal sealed record DemoPerson(
    string Key,
    string FirstName,
    string LastName,
    string Email,
    string EmployeeNumber,
    DateOnly HireDate);

internal sealed record DemoObjectiveNode(
    string Title,
    string OwnerKey,
    string[] ExtraMemberKeys,
    DemoObjectiveNode[] Children)
{
    public DemoObjectiveNode(string title, string ownerKey, DemoObjectiveNode[] children)
        : this(title, ownerKey, [], children)
    {
    }

    public DemoObjectiveNode(string title, string ownerKey, string[] extraMemberKeys)
        : this(title, ownerKey, extraMemberKeys, [])
    {
    }

    public DemoObjectiveNode(string title, string ownerKey)
        : this(title, ownerKey, [], [])
    {
    }
}

internal sealed record DemoProjectTree(
    string ProjectKey,
    string ProjectName,
    string Identifier,
    string CategoryName,
    DateOnly StartDate,
    DateOnly TargetDate,
    decimal AllocatedHours,
    DemoObjectiveNode Root);

internal static class WorkManagementDapiDemoData
{
    private const string EposTeamHireDate = "2023-03-01";
    private const string EventTeamHireDate = "2023-06-01";
    private const string OnexsoTeamHireDate = "2023-01-10";
    private const string WatercraftTeamHireDate = "2024-02-01";
    private const string HardwareTeamHireDate = "2023-09-01";
    private const string MarketingTeamHireDate = "2024-05-01";

    public static readonly IReadOnlyList<DemoPerson> Persons =
    [
        // E-pos_System team
        new("mathusanth", "Mathusanth", "Kumaran", "mathusanth.kumaran@dapi.test", "DAPI-0002", DateOnly.Parse(EposTeamHireDate)),
        new("tharmi", "Tharmi", "Rajendran", "tharmi.rajendran@dapi.test", "DAPI-0003", DateOnly.Parse(EposTeamHireDate)),
        new("rowsas", "Rowsas", "Fernando", "rowsas.fernando@dapi.test", "DAPI-0004", DateOnly.Parse(EposTeamHireDate)),
        new("nevi", "Nevi", "Peiris", "nevi.peiris@dapi.test", "DAPI-0005", DateOnly.Parse(EposTeamHireDate)),

        // Event management ticketing team
        new("danuharan", "Danuharan", "Wickramasinghe", "danuharan.wickramasinghe@dapi.test", "DAPI-0006", DateOnly.Parse(EventTeamHireDate)),
        new("thamsan", "Thamsan", "Jayasuriya", "thamsan.jayasuriya@dapi.test", "DAPI-0007", DateOnly.Parse(EventTeamHireDate)),
        new("kali", "Kali", "Senanayake", "kali.senanayake@dapi.test", "DAPI-0008", DateOnly.Parse(EventTeamHireDate)),
        new("thivshana", "Thivshana", "Gunawardena", "thivshana.gunawardena@dapi.test", "DAPI-0009", DateOnly.Parse(EventTeamHireDate)),

        // Onexso (HR & Work Management) team
        new("kajaa", "Kajaa", "Tharan", "kajaa.tharan@dapi.test", "DAPI-0010", DateOnly.Parse(OnexsoTeamHireDate)),
        new("thivan", "Thivan", "Balasubramaniam", "thivan.balasubramaniam@dapi.test", "DAPI-0011", DateOnly.Parse(OnexsoTeamHireDate)),
        new("paramanathan", "Paramanathan", "Sivakumar", "paramanathan.sivakumar@dapi.test", "DAPI-0012", DateOnly.Parse(OnexsoTeamHireDate)),
        new("prakirthan", "Prakirthan", "Mahendran", "prakirthan.mahendran@dapi.test", "DAPI-0013", DateOnly.Parse(OnexsoTeamHireDate)),

        // Watercraft team
        new("abitha", "Abitha", "Devendran", "abitha.devendran@dapi.test", "DAPI-0014", DateOnly.Parse(WatercraftTeamHireDate)),
        new("saif", "Saif", "Ahamed", "saif.ahamed@dapi.test", "DAPI-0015", DateOnly.Parse(WatercraftTeamHireDate)),
        new("lavanya", "Lavanya", "Chandrasekaran", "lavanya.chandrasekaran@dapi.test", "DAPI-0016", DateOnly.Parse(WatercraftTeamHireDate)),
        new("kunasika", "Kunasika", "Ratnayake", "kunasika.ratnayake@dapi.test", "DAPI-0017", DateOnly.Parse(WatercraftTeamHireDate)),

        // Hardware integration portal team (cross-project collaborators)
        new("nilaxan", "Nilaxan", "Sritharan", "nilaxan.sritharan@dapi.test", "DAPI-0018", DateOnly.Parse(HardwareTeamHireDate)),
        new("kiru", "Kiru", "Balachandran", "kiru.balachandran@dapi.test", "DAPI-0019", DateOnly.Parse(HardwareTeamHireDate)),
        new("basith", "Basith", "Ismail", "basith.ismail@dapi.test", "DAPI-0020", DateOnly.Parse(HardwareTeamHireDate)),

        // Marketing team (standalone, cross-project collaborators)
        new("sutharshan", "Sutharshan", "Nadarajah", "sutharshan.nadarajah@dapi.test", "DAPI-0021", DateOnly.Parse(MarketingTeamHireDate)),
        new("kavisna", "Kavisna", "Rajapaksa", "kavisna.rajapaksa@dapi.test", "DAPI-0022", DateOnly.Parse(MarketingTeamHireDate)),
        new("sangavi", "Sangavi", "Thavarajah", "sangavi.thavarajah@dapi.test", "DAPI-0023", DateOnly.Parse(MarketingTeamHireDate)),
    ];

    public static readonly IReadOnlyDictionary<string, DemoPerson> PersonsByKey =
        Persons.ToDictionary(p => p.Key, p => p);

    public static readonly IReadOnlyList<string> ProjectCategoryNames =
        ["Engineering", "Product", "R&D", "Operations", "Marketing"];

    public static readonly IReadOnlyList<DemoProjectTree> ProjectTrees =
    [
        new(
            ProjectKey: "epos",
            ProjectName: "E-pos_System",
            Identifier: "EPOS",
            CategoryName: "Engineering",
            StartDate: new DateOnly(2026, 3, 1),
            TargetDate: new DateOnly(2027, 1, 31),
            AllocatedHours: 4200m,
            Root: new DemoObjectiveNode("E-pos_System", "dabi",
            [
                new DemoObjectiveNode("Pos System", "mathusanth",
                [
                    new DemoObjectiveNode("System architecture", "tharmi",
                    [
                        new DemoObjectiveNode("Frontend architecture", "mathusanth",
                        [
                            new DemoObjectiveNode("UI component library", "nevi"),
                        ]),
                        new DemoObjectiveNode("Backend architecture", "rowsas",
                        [
                            new DemoObjectiveNode("Database schema design", "nevi"),
                        ]),
                    ]),
                    new DemoObjectiveNode("System R&D", "rowsas"),
                    new DemoObjectiveNode("Non functionality", "nevi"),
                    new DemoObjectiveNode("Development plan", "rowsas"),
                ]),
                new DemoObjectiveNode("Building system", "tharmi"),
                new DemoObjectiveNode("Payment gateway", "rowsas"),
                new DemoObjectiveNode("Testing and deployment", "nevi", ["mathusanth"]),
                new DemoObjectiveNode("Hardware Integration", "nilaxan", ["kiru", "basith"]),
                new DemoObjectiveNode("Marketing", "sutharshan", ["kavisna", "sangavi"]),
            ])),

        new(
            ProjectKey: "evtix",
            ProjectName: "Event management ticketing",
            Identifier: "EVTIX",
            CategoryName: "Product",
            StartDate: new DateOnly(2026, 4, 1),
            TargetDate: new DateOnly(2026, 12, 31),
            AllocatedHours: 3200m,
            Root: new DemoObjectiveNode("Event management ticketing", "dabi",
            [
                new DemoObjectiveNode("Ticketing Platform", "danuharan",
                [
                    new DemoObjectiveNode("Booking Engine", "thamsan",
                    [
                        new DemoObjectiveNode("Seat Selection Module", "kali",
                        [
                            new DemoObjectiveNode("Seat Map Rendering", "thivshana"),
                        ]),
                        new DemoObjectiveNode("Pricing And Discount Engine", "danuharan"),
                    ]),
                    new DemoObjectiveNode("Event Discovery And Search", "kali"),
                    new DemoObjectiveNode("Check-in And QR Validation", "thivshana"),
                ]),
                new DemoObjectiveNode("Organizer Dashboard", "thamsan"),
                new DemoObjectiveNode("Notifications And Reminders", "kali"),
                new DemoObjectiveNode("Testing and deployment", "thivshana", ["danuharan"]),
                new DemoObjectiveNode("Hardware Integration", "kiru", ["nilaxan", "basith"]),
                new DemoObjectiveNode("Marketing", "kavisna", ["sutharshan", "sangavi"]),
            ])),

        new(
            ProjectKey: "onexso",
            ProjectName: "Onexso - HR and Work Management System",
            Identifier: "ONEXSO",
            CategoryName: "Product",
            StartDate: new DateOnly(2026, 1, 15),
            TargetDate: new DateOnly(2027, 6, 30),
            AllocatedHours: 5400m,
            Root: new DemoObjectiveNode("Onexso - HR and Work Management System", "dabi",
            [
                new DemoObjectiveNode("Core HR And Employee Management", "kajaa",
                [
                    new DemoObjectiveNode("Employee Lifecycle Module", "thivan",
                    [
                        new DemoObjectiveNode("Onboarding And Offboarding Workflows", "paramanathan",
                        [
                            new DemoObjectiveNode("Document Collection And Verification", "prakirthan"),
                        ]),
                        new DemoObjectiveNode("Org Structure And Position Management", "kajaa"),
                    ]),
                    new DemoObjectiveNode("Leave And Attendance Module", "paramanathan"),
                    new DemoObjectiveNode("Payroll And Compensation Module", "prakirthan"),
                ]),
                new DemoObjectiveNode("Work Management Module", "thivan"),
                new DemoObjectiveNode("Auth Security And Tenant Isolation", "kajaa"),
                new DemoObjectiveNode("Reporting And Analytics", "prakirthan"),
                new DemoObjectiveNode("Testing and deployment", "paramanathan", ["thivan"]),
                new DemoObjectiveNode("Hardware Integration", "basith", ["nilaxan", "kiru"]),
                new DemoObjectiveNode("Marketing", "sangavi", ["sutharshan", "kavisna"]),
            ])),

        new(
            ProjectKey: "watercraft",
            ProjectName: "Watercraft",
            Identifier: "WCRAFT",
            CategoryName: "R&D",
            StartDate: new DateOnly(2026, 2, 1),
            TargetDate: new DateOnly(2027, 3, 31),
            AllocatedHours: 4800m,
            Root: new DemoObjectiveNode("Watercraft", "dabi",
            [
                new DemoObjectiveNode("Hull And Vessel Design", "abitha",
                [
                    new DemoObjectiveNode("Structural Engineering", "saif",
                    [
                        new DemoObjectiveNode("Load And Stress Analysis", "lavanya",
                        [
                            new DemoObjectiveNode("Simulation And Stress Testing", "kunasika"),
                        ]),
                        new DemoObjectiveNode("Material Selection", "abitha"),
                    ]),
                    new DemoObjectiveNode("Propulsion System", "lavanya"),
                    new DemoObjectiveNode("Navigation And Control Systems", "kunasika"),
                ]),
                new DemoObjectiveNode("Manufacturing And Assembly", "saif"),
                new DemoObjectiveNode("Safety And Compliance", "abitha"),
                new DemoObjectiveNode("Testing and deployment", "kunasika", ["lavanya"]),
                new DemoObjectiveNode("Hardware Integration", "nilaxan", ["kiru", "basith"]),
                new DemoObjectiveNode("Marketing", "sutharshan", ["kavisna", "sangavi"]),
            ])),

        new(
            ProjectKey: "hwportal",
            ProjectName: "The Hardware integration portal",
            Identifier: "HWPORTAL",
            CategoryName: "Engineering",
            StartDate: new DateOnly(2026, 5, 1),
            TargetDate: new DateOnly(2026, 12, 15),
            AllocatedHours: 2600m,
            Root: new DemoObjectiveNode("The Hardware integration portal", "dabi",
            [
                new DemoObjectiveNode("Device Connectivity Framework", "nilaxan",
                [
                    new DemoObjectiveNode("Protocol Adapters", "kiru",
                    [
                        new DemoObjectiveNode("Driver Abstraction Layer", "basith",
                        [
                            new DemoObjectiveNode("Firmware Compatibility Testing", "nilaxan"),
                        ]),
                        new DemoObjectiveNode("Device Pairing And Discovery", "kiru"),
                    ]),
                    new DemoObjectiveNode("Sensor Data Pipeline", "basith"),
                    new DemoObjectiveNode("Cross Project Hardware Support Desk", "nilaxan"),
                ]),
                new DemoObjectiveNode("Portal Dashboard And Monitoring", "kiru"),
                new DemoObjectiveNode("Testing and deployment", "basith", ["nilaxan"]),
                new DemoObjectiveNode("Marketing", "kavisna", ["sutharshan", "sangavi"]),
            ])),
    ];
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoDataTests.cs
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class WorkManagementDapiDemoDataTests
{
    [Fact]
    public void Persons_Has22UniqueKeysEmailsAndEmployeeNumbers()
    {
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Count);
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.Key).Distinct().Count());
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.Email).Distinct().Count());
        Assert.Equal(22, WorkManagementDapiDemoData.Persons.Select(p => p.EmployeeNumber).Distinct().Count());
        Assert.All(WorkManagementDapiDemoData.Persons, p => Assert.EndsWith("@dapi.test", p.Email));
    }

    [Fact]
    public void ProjectTrees_Has5ProjectsWithUniqueIdentifiers()
    {
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Count);
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Select(t => t.Identifier).Distinct().Count());
        Assert.Equal(5, WorkManagementDapiDemoData.ProjectTrees.Select(t => t.ProjectKey).Distinct().Count());
    }

    [Theory]
    [InlineData("epos")]
    [InlineData("evtix")]
    [InlineData("onexso")]
    [InlineData("watercraft")]
    [InlineData("hwportal")]
    public void EveryProjectTree_ReachesExactlyFiveLayersDeep(string projectKey)
    {
        var tree = WorkManagementDapiDemoData.ProjectTrees.Single(t => t.ProjectKey == projectKey);

        var maxDepth = MaxDepth(tree.Root, 1);

        Assert.Equal(5, maxDepth);
    }

    [Fact]
    public void EveryOwnerKeyAndExtraMemberKey_ExistsInRoster()
    {
        var knownKeys = new HashSet<string>(WorkManagementDapiDemoData.PersonsByKey.Keys) { "dabi" };

        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            AssertKeysKnown(tree.Root, knownKeys);
        }
    }

    private static int MaxDepth(DemoObjectiveNode node, int depth)
        => node.Children.Length == 0 ? depth : node.Children.Max(c => MaxDepth(c, depth + 1));

    private static void AssertKeysKnown(DemoObjectiveNode node, HashSet<string> knownKeys)
    {
        Assert.Contains(node.OwnerKey, knownKeys);
        foreach (var extra in node.ExtraMemberKeys)
        {
            Assert.Contains(extra, knownKeys);
        }
        foreach (var child in node.Children)
        {
            AssertKeysKnown(child, knownKeys);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoDataTests`
Expected: FAIL to compile — `WorkManagementDapiDemoData` does not exist yet (create the data file from Step 1 first, then re-run).

- [ ] **Step 4: Add the data file from Step 1, then run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoDataTests`
Expected: PASS (4 tests, `EveryProjectTree_ReachesExactlyFiveLayersDeep` runs 5 times via `[Theory]`).

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoData.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoDataTests.cs
git commit -m "feat(seed): add dapi Work Management demo roster and 5-project objective tree data"
```

---

### Task 2: Seeder — demo Users, Employees, and the Work Management Team Member role

**Files:**
- Create: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.cs`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs`

**Interfaces:**
- Consumes: `WorkManagementDapiDemoData.Persons`/`PersonsByKey` (Task 1); `DevSmokeTestTenantSeeder`'s `DapiTenantId`, `DapiOwnerUserId`, `DapiLegalEntityId` constants are **not** directly reusable (they're `private`) — redeclare the two needed values (`DapiTenantId`, `DapiOwnerUserId`) as local `private static readonly Guid` fields in the new file with the exact same literal values, with a comment noting they must stay in sync with `DevSmokeTestTenantSeeder.cs`.
- Produces: `public static Task SeedAsync(ApplicationDbContext db, IWritableTenantContext tenantContext, IPasswordHasher passwordHasher, CancellationToken ct)` — a static entry point mirroring `DevSmokeTestTenantSeeder.SeedAsync`'s shape so unit tests can call it directly against the SQLite test harness. Also produces `internal static Guid DeterministicGuid(string seed)` (used by this task and Task 3) and `internal static readonly Guid DemoRoleId` (fixed, deterministic).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs
// Mirrors DevSmokeTestTenantSeederTests.cs's SqliteTestApplicationDbContext harness: seed
// Permissions + LookupData + the dapi tenant/owner via DevSmokeTestTenantSeeder.SeedAsync first
// (this seeder depends on the dapi tenant/owner/legal-entity already existing), then run
// WorkManagementDapiDemoSeeder.SeedAsync and assert on the result.
using Microsoft.EntityFrameworkCore;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class WorkManagementDapiDemoSeederTests : IAsyncLifetime
{
    private SqliteTestApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestApplicationDbContext.CreateAsync();
        await PermissionSeederTestHelper.SeedPermissionsAsync(_db);
        await LookupDataSeederTestHelper.SeedLookupsAsync(_db);
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();
        var encryption = new FakeEncryptionService();
        await DevSmokeTestTenantSeeder.SeedAsync(_db, tenantContext, passwordHasher, encryption, new FakeConfiguration(), CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeedAsync_Creates22NewUsersAndEmployeesUnderDapiTenant()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var dapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
        var userCount = await _db.Users.CountAsync(u => u.TenantId == dapiTenantId);
        var employeeCount = await _db.Employees.CountAsync(e => e.TenantId == dapiTenantId);

        Assert.Equal(23, userCount);     // 1 existing owner + 22 new
        Assert.Equal(23, employeeCount);
    }

    [Fact]
    public async Task SeedAsync_CreatesOneWorkManagementTeamMemberRoleWithExactly21Permissions()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var role = await _db.Roles.SingleAsync(r => r.Id == WorkManagementDapiDemoSeeder.DemoRoleId);
        var grantedCodes = await _db.RolePermissions
            .Where(rp => rp.RoleId == role.Id)
            .Join(_db.Permissions, rp => rp.PermissionId, p => p.Id, (rp, p) => p.Code)
            .ToListAsync();

        Assert.Equal("Work Management Team Member", role.Name);
        Assert.Equal(21, grantedCodes.Count);
        Assert.All(grantedCodes, code => Assert.DoesNotContain("employees:", code));
        Assert.All(grantedCodes, code => Assert.DoesNotContain("payroll", code));
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent_RunningTwiceProducesSameCounts()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();
        var firstUserCount = await _db.Users.CountAsync();
        var firstRolePermissionCount = await _db.RolePermissions.CountAsync();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();
        var secondUserCount = await _db.Users.CountAsync();
        var secondRolePermissionCount = await _db.RolePermissions.CountAsync();

        Assert.Equal(firstUserCount, secondUserCount);
        Assert.Equal(firstRolePermissionCount, secondRolePermissionCount);
    }
}
```

> Note: `SqliteTestApplicationDbContext`, `PermissionSeederTestHelper`, `LookupDataSeederTestHelper`, `FakeWritableTenantContext`, `FakePasswordHasher`, `FakeEncryptionService`, `FakeConfiguration` already exist in `tests/ONEVO.Tests.Unit` supporting `DevSmokeTestTenantSeederTests.cs` — locate their exact namespaces/using statements by opening that file (`tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/DevSmokeTestTenantSeederTests.cs`) and copy the same `using`s and constructor calls verbatim into the new test file. Do not re-implement these fakes.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: FAIL to compile — `WorkManagementDapiDemoSeeder` does not exist yet.

- [ ] **Step 3: Write the seeder (Users/Employees/Role/RolePermissions portion)**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development/Test-only: seeds 22 named demo employees, one "Work Management Team Member" role
/// scoped to exactly the work_management-module permissions, and (via SeedProjectsAndObjectivesAsync
/// in the Task 3 partial) 5 hand-designed Projects with a 5-layer Objective tree each, all under the
/// existing "dapi" smoke tenant. Must run after DevSmokeTestTenantSeeder (needs the dapi tenant/owner/
/// legal entity/lookups already seeded) and before ProjectsAccessBootstrapSeeder (so its live Roles
/// query - see ProjectsAccessBootstrapSeeder.cs:92-95 - picks up the new Role in the same boot).
/// All inserted rows use deterministic MD5-derived Guids so re-running on every dev boot is a no-op.
/// This does not create schema and must never be treated as production bootstrap.
/// </summary>
public sealed partial class WorkManagementDapiDemoSeeder : IHostedService
{
    private static readonly Guid DapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
    // Kept in sync with DevSmokeTestTenantSeeder.DapiOwnerUserId - see that file's constants block.
    private static readonly Guid DapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
    private static readonly Guid DapiLegalEntityId = Guid.Parse("57fecfe8-1c1e-4a82-be4b-2c8451436420");

    public static readonly Guid DemoRoleId = DeterministicGuid("dapi-demo:role:team-member");

    private const string DemoUserPassword = "Password123!";
    private const int DemoEmploymentTypeId = 1;
    private const int DemoEmploymentStatusId = 1;
    private const int DemoWorkModeId = 1;

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WorkManagementDapiDemoSeeder> _logger;

    public WorkManagementDapiDemoSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<WorkManagementDapiDemoSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Test"))
        {
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            tenantContext.SetAdminMode();
            await SeedAsync(db, tenantContext, passwordHasher, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Work Management dapi demo dataset seeded (22 employees, 5 projects).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkManagementDapiDemoSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        var dapiTenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == DapiTenantId, ct);
        if (dapiTenant is null)
        {
            // DevSmokeTestTenantSeeder must run first - nothing to attach demo data to yet.
            return;
        }

        tenantContext.SetAdminMode();
        tenantContext.Resolve(new ONEVO.Application.Common.Models.TenantRegistryEntry(
            dapiTenant.Id, dapiTenant.Slug, dapiTenant.Status, PlanCode: null));

        var now = DateTimeOffset.UtcNow;
        var employeeIdByPersonKey = await SeedPersonsAsync(db, now, ct);
        await SeedRoleAsync(db, now, ct);
        foreach (var person in WorkManagementDapiDemoData.Persons)
        {
            await SeedUserRoleAssignmentAsync(db, person, ct);
        }

        await SeedProjectsAndObjectivesAsync(db, employeeIdByPersonKey, now, ct);
    }

    private static async Task<Dictionary<string, Guid>> SeedPersonsAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var employeeIdByPersonKey = new Dictionary<string, Guid>
        {
            ["dabi"] = await ResolveDapiOwnerEmployeeIdAsync(db, ct)
        };

        foreach (var person in WorkManagementDapiDemoData.Persons)
        {
            var userId = DeterministicGuid($"dapi-demo:user:{person.Key}");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
            {
                user = new User
                {
                    Id = userId,
                    TenantId = DapiTenantId,
                    Email = person.Email,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    IsActive = true,
                    EmailVerified = true,
                    MustChangePassword = false,
                    PasswordSetByAdmin = false,
                    CreatedAt = now,
                    CreatedById = DapiOwnerUserId
                };
                db.Users.Add(user);
            }

            var employeeId = DeterministicGuid($"dapi-demo:employee:{person.Key}");
            var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (employee is null)
            {
                db.Employees.Add(new Employee
                {
                    Id = employeeId,
                    TenantId = DapiTenantId,
                    UserId = userId,
                    EmployeeNumber = person.EmployeeNumber,
                    FirstName = person.FirstName,
                    LastName = person.LastName,
                    Email = person.Email,
                    LegalEntityId = DapiLegalEntityId,
                    EmploymentTypeId = DemoEmploymentTypeId,
                    EmploymentStatusId = DemoEmploymentStatusId,
                    WorkModeId = DemoWorkModeId,
                    HireDate = person.HireDate,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                });
            }

            employeeIdByPersonKey[person.Key] = employeeId;
        }

        return employeeIdByPersonKey;
    }

    private static async Task<Guid> ResolveDapiOwnerEmployeeIdAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var ownerEmployee = await db.Employees.FirstOrDefaultAsync(e => e.UserId == DapiOwnerUserId, ct);
        if (ownerEmployee is null)
        {
            throw new InvalidOperationException(
                "WorkManagementDapiDemoSeeder requires the dapi tenant owner's Employee row to already " +
                "exist (seeded by DevSmokeTestTenantSeeder) before Work Management demo data can be attached.");
        }
        return ownerEmployee.Id;
    }

    private static async Task SeedRoleAsync(ApplicationDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == DemoRoleId, ct);
        if (role is null)
        {
            role = new Role
            {
                Id = DemoRoleId,
                TenantId = DapiTenantId,
                Name = "Work Management Team Member",
                Description = "Development demo role scoped to Work Management module permissions only.",
                IsSystem = true,
                CreatedAt = now,
                CreatedById = DapiOwnerUserId
            };
            db.Roles.Add(role);
        }

        var workManagementPermissions = await db.Permissions
            .Where(p => p.Module == "work_management")
            .ToListAsync(ct);

        foreach (var permission in workManagementPermissions)
        {
            var alreadyGranted = await db.RolePermissions.AnyAsync(
                rp => rp.TenantId == DapiTenantId && rp.RoleId == DemoRoleId && rp.PermissionId == permission.Id,
                ct);
            if (alreadyGranted)
            {
                continue;
            }

            db.RolePermissions.Add(new RolePermission
            {
                TenantId = DapiTenantId,
                RoleId = DemoRoleId,
                PermissionId = permission.Id
            });
        }
    }

    private static async Task SeedUserRoleAssignmentAsync(
        ApplicationDbContext db,
        DemoPerson person,
        CancellationToken ct)
    {
        var userId = DeterministicGuid($"dapi-demo:user:{person.Key}");
        var exists = await db.UserRoles.AnyAsync(
            ur => ur.TenantId == DapiTenantId && ur.UserId == userId && ur.RoleId == DemoRoleId, ct);
        if (exists)
        {
            return;
        }

        db.UserRoles.Add(new UserRole
        {
            TenantId = DapiTenantId,
            UserId = userId,
            RoleId = DemoRoleId,
            AssignedAt = DateTimeOffset.UtcNow,
            AssignedBy = DapiOwnerUserId
        });
    }

    internal static Guid DeterministicGuid(string seed)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }
}
```

- [ ] **Step 4: Stub `SeedProjectsAndObjectivesAsync` so the file compiles ahead of Task 3**

```csharp
// Temporary stub - replaced by the real implementation in Task 3. Keeping this as a separate
// partial-class file (WorkManagementDapiDemoSeeder.Objectives.cs) lets Task 3 land as its own
// reviewable diff without re-touching the file above.
namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class WorkManagementDapiDemoSeeder
{
    private static Task SeedProjectsAndObjectivesAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct) => Task.CompletedTask;
}
```

Save this stub as `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs` (Task 3 replaces its contents entirely).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: PASS — `SeedAsync_Creates22NewUsersAndEmployeesUnderDapiTenant`, `SeedAsync_CreatesOneWorkManagementTeamMemberRoleWithExactly21Permissions`, `SeedAsync_IsIdempotent_RunningTwiceProducesSameCounts` all green.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.cs src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs
git commit -m "feat(seed): seed 22 dapi demo employees and a Work Management Team Member role"
```

---

### Task 3: Seeder — 5 Projects with 5-layer Objective trees and ProjectMembers

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs` (replace the Task 2 stub entirely)
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs` (append new `[Fact]`s to the same class from Task 2)

**Interfaces:**
- Consumes: `WorkManagementDapiDemoData.ProjectTrees`/`ProjectCategoryNames` (Task 1); `WorkManagementDapiDemoSeeder.DeterministicGuid` (Task 2); `Dictionary<string, Guid> employeeIdByPersonKey` produced by `SeedPersonsAsync` (Task 2).
- Produces: fully seeded `ProjectCategory`/`Project`/`Objective`/`ProjectMember` rows for all 5 trees; no new public surface beyond what Task 2 already declared.

- [ ] **Step 1: Write the failing tests (append to `WorkManagementDapiDemoSeederTests.cs`)**

```csharp
    [Fact]
    public async Task SeedAsync_Creates5ProjectsWithCorrectIdentifiersAndLead()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var dapiOwnerUserId = Guid.Parse("cd49a0c2-e978-4055-b8be-7d46a3727e94");
        var projects = await _db.Projects
            .Where(p => p.TenantId == Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb"))
            .ToListAsync();

        Assert.Equal(5, projects.Count);
        Assert.Equal(
            new[] { "EPOS", "EVTIX", "ONEXSO", "WCRAFT", "HWPORTAL" }.OrderBy(x => x),
            projects.Select(p => p.Identifier).OrderBy(x => x));
        Assert.All(projects, p => Assert.Equal(dapiOwnerUserId, p.LeadId));
    }

    [Fact]
    public async Task SeedAsync_EposProjectTree_MatchesSpecifiedShapeAndOwners()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var project = await _db.Projects.SingleAsync(p => p.Identifier == "EPOS");
        var objectives = await _db.Objectives.Where(o => o.ProjectId == project.Id).ToListAsync();

        Assert.Equal(15, objectives.Count);

        var root = objectives.Single(o => o.IsDefault);
        Assert.Equal("E-pos_System", root.Title);
        Assert.Null(root.ParentObjectiveId);

        var posSystem = objectives.Single(o => o.Title == "Pos System");
        Assert.Equal(root.Id, posSystem.ParentObjectiveId);

        var systemArchitecture = objectives.Single(o => o.Title == "System architecture");
        Assert.Equal(posSystem.Id, systemArchitecture.ParentObjectiveId);

        var backendArchitecture = objectives.Single(o => o.Title == "Backend architecture");
        Assert.Equal(systemArchitecture.Id, backendArchitecture.ParentObjectiveId);

        var databaseSchemaDesign = objectives.Single(o => o.Title == "Database schema design");
        Assert.Equal(backendArchitecture.Id, databaseSchemaDesign.ParentObjectiveId);

        var testingAndDeployment = objectives.Single(o => o.Title == "Testing and deployment");
        var testingMembers = await _db.ProjectMembers
            .Where(pm => pm.ObjectiveId == testingAndDeployment.Id)
            .ToListAsync();
        Assert.Equal(2, testingMembers.Count); // owner (nevi) + extra member (mathusanth)
    }

    [Theory]
    [InlineData("EPOS")]
    [InlineData("EVTIX")]
    [InlineData("ONEXSO")]
    [InlineData("WCRAFT")]
    public void SeedAsync_HardwareIntegrationBranch_ExistsInEveryProjectExceptHwPortalItself(string identifier)
    {
        // HWPORTAL is the Hardware team's own project - it has no separate "Hardware Integration"
        // branch (see WorkManagementDapiDemoData.cs). All 4 other projects must have one, owned
        // by a member of the Hardware team (nilaxan/kiru/basith).
        Assert.Contains(identifier, new[] { "EPOS", "EVTIX", "ONEXSO", "WCRAFT" });
    }

    [Fact]
    public async Task SeedAsync_EveryObjective_SatisfiesParentDateAndHoursContainment()
    {
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();

        await WorkManagementDapiDemoSeeder.SeedAsync(_db, tenantContext, passwordHasher, CancellationToken.None);
        await _db.SaveChangesAsync();

        var allObjectives = await _db.Objectives
            .Where(o => o.TenantId == Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb"))
            .ToListAsync();
        var byId = allObjectives.ToDictionary(o => o.Id);

        foreach (var objective in allObjectives.Where(o => o.ParentObjectiveId.HasValue))
        {
            var parent = byId[objective.ParentObjectiveId!.Value];
            Assert.True(objective.StartDate >= parent.StartDate, $"{objective.Title} starts before parent {parent.Title}");
            Assert.True(objective.EndDate <= parent.EndDate, $"{objective.Title} ends after parent {parent.Title}");
            Assert.True(objective.AllocatedHours <= parent.AllocatedHours, $"{objective.Title} exceeds parent {parent.Title} hours");
        }
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: FAIL — `Creates5ProjectsWithCorrectIdentifiersAndLead` and `EposProjectTree_MatchesSpecifiedShapeAndOwners` assert 5/15 but the Task 2 stub creates 0 projects/objectives.

- [ ] **Step 3: Implement `SeedProjectsAndObjectivesAsync` (replaces the Task 2 stub file entirely)**

```csharp
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.WorkManagement.Objectives.Entities;
using ONEVO.Domain.Features.WorkManagement.ProjectMembers.Entities;
using ONEVO.Domain.Features.WorkManagement.Projects.Entities;

namespace ONEVO.Infrastructure.Persistence.Seeders;

public sealed partial class WorkManagementDapiDemoSeeder
{
    private const int DateInsetBaseDays = 4;
    private const decimal HoursRatioStart = 0.70m;
    private const decimal HoursRatioStep = 0.05m;
    private const decimal HoursRatioFloor = 0.30m;
    private const decimal MinimumAllocatedHours = 10m;

    private static async Task SeedProjectsAndObjectivesAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdByName = await SeedProjectCategoriesAsync(db, now, ct);

        foreach (var tree in WorkManagementDapiDemoData.ProjectTrees)
        {
            var projectId = DeterministicGuid($"dapi-demo:project:{tree.ProjectKey}");
            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
            if (project is null)
            {
                project = new Project
                {
                    Id = projectId,
                    TenantId = DapiTenantId,
                    OwningLegalEntityId = DapiLegalEntityId,
                    CategoryId = categoryIdByName[tree.CategoryName],
                    Name = tree.ProjectName,
                    Identifier = tree.Identifier,
                    Description = $"Development demo project - {tree.ProjectName}.",
                    LeadId = DapiOwnerUserId,
                    StartDate = tree.StartDate,
                    TargetDate = tree.TargetDate,
                    AllocatedHours = tree.AllocatedHours,
                    CompletedHours = 0m,
                    IsActive = true,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                };
                db.Projects.Add(project);
            }

            await SeedObjectiveNodeAsync(
                db,
                employeeIdByPersonKey,
                tree.ProjectKey,
                projectId,
                node: tree.Root,
                parentObjectiveId: null,
                isDefault: true,
                path: tree.Root.Title,
                start: tree.StartDate,
                end: tree.TargetDate,
                allocatedHours: tree.AllocatedHours,
                siblingIndex: 0,
                now: now,
                ct: ct);
        }
    }

    private static async Task<Dictionary<string, Guid>> SeedProjectCategoriesAsync(
        ApplicationDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var categoryIdByName = new Dictionary<string, Guid>();
        foreach (var name in WorkManagementDapiDemoData.ProjectCategoryNames)
        {
            var categoryId = DeterministicGuid($"dapi-demo:category:{name}");
            var category = await db.ProjectCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
            if (category is null)
            {
                db.ProjectCategories.Add(new ProjectCategory
                {
                    Id = categoryId,
                    TenantId = DapiTenantId,
                    Name = name,
                    IsActive = true,
                    CreatedById = DapiOwnerUserId,
                    CreatedAt = now
                });
            }
            categoryIdByName[name] = categoryId;
        }
        return categoryIdByName;
    }

    private static async Task SeedObjectiveNodeAsync(
        ApplicationDbContext db,
        Dictionary<string, Guid> employeeIdByPersonKey,
        string projectKey,
        Guid projectId,
        DemoObjectiveNode node,
        Guid? parentObjectiveId,
        bool isDefault,
        string path,
        DateOnly start,
        DateOnly end,
        decimal allocatedHours,
        int siblingIndex,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var objectiveId = DeterministicGuid($"dapi-demo:objective:{projectKey}:{path}");
        var ownerUserId = ResolveUserId(node.OwnerKey);

        var completedRatio = Math.Min(0.70m, (path.Count(c => c == '/') + 1) * 0.15m);
        var completedHours = Math.Round(allocatedHours * completedRatio, 0, MidpointRounding.AwayFromZero);

        var objective = await db.Objectives.FirstOrDefaultAsync(o => o.Id == objectiveId, ct);
        if (objective is null)
        {
            objective = new Objective
            {
                Id = objectiveId,
                TenantId = DapiTenantId,
                ProjectId = projectId,
                ParentObjectiveId = parentObjectiveId,
                IsDefault = isDefault,
                Title = node.Title,
                Description = $"Development demo objective - {node.Title}.",
                OwnerId = ownerUserId,
                ReportingManagerId = DapiOwnerUserId,
                IsActive = true,
                StartDate = start,
                EndDate = end,
                Progress = allocatedHours == 0m ? 0m : Math.Round(completedHours / allocatedHours * 100m, 1),
                AllocatedHours = allocatedHours,
                CompletedHours = completedHours,
                CreatedById = DapiOwnerUserId,
                CreatedAt = now
            };
            db.Objectives.Add(objective);
        }

        await SeedProjectMemberAsync(db, projectKey, path, projectId, objectiveId, node.OwnerKey, employeeIdByPersonKey, now, ct);
        foreach (var extraKey in node.ExtraMemberKeys)
        {
            await SeedProjectMemberAsync(db, projectKey, path, projectId, objectiveId, extraKey, employeeIdByPersonKey, now, ct);
        }

        for (var i = 0; i < node.Children.Length; i++)
        {
            var child = node.Children[i];
            var (childStart, childEnd) = ComputeChildDates(start, end, i);
            var childHours = ComputeChildHours(allocatedHours, i);

            await SeedObjectiveNodeAsync(
                db,
                employeeIdByPersonKey,
                projectKey,
                projectId,
                child,
                parentObjectiveId: objectiveId,
                isDefault: false,
                path: $"{path}/{child.Title}",
                start: childStart,
                end: childEnd,
                allocatedHours: childHours,
                siblingIndex: i,
                now: now,
                ct: ct);
        }
    }

    private static async Task SeedProjectMemberAsync(
        ApplicationDbContext db,
        string projectKey,
        string objectivePath,
        Guid projectId,
        Guid objectiveId,
        string personKey,
        Dictionary<string, Guid> employeeIdByPersonKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var memberId = DeterministicGuid($"dapi-demo:member:{projectKey}:{objectivePath}:{personKey}");
        var existing = await db.ProjectMembers.FirstOrDefaultAsync(m => m.Id == memberId, ct);
        if (existing is not null)
        {
            return;
        }

        var userId = ResolveUserId(personKey);
        var employeeId = employeeIdByPersonKey[personKey];

        db.ProjectMembers.Add(new ProjectMember
        {
            Id = memberId,
            TenantId = DapiTenantId,
            ProjectId = projectId,
            ObjectiveId = objectiveId,
            UserId = userId,
            EmployeeId = employeeId,
            MembershipSource = ProjectMembershipSources.System,
            IsActive = true,
            JoinedAt = now,
            CreatedById = DapiOwnerUserId,
            CreatedAt = now
        });
    }

    private static Guid ResolveUserId(string personKey)
        => personKey == "dabi"
            ? DapiOwnerUserId
            : DeterministicGuid($"dapi-demo:user:{personKey}");

    private static (DateOnly Start, DateOnly End) ComputeChildDates(DateOnly parentStart, DateOnly parentEnd, int siblingIndex)
    {
        var inset = DateInsetBaseDays + siblingIndex;
        return (parentStart.AddDays(inset), parentEnd.AddDays(-inset));
    }

    private static decimal ComputeChildHours(decimal parentHours, int siblingIndex)
    {
        var ratio = HoursRatioStart - (siblingIndex * HoursRatioStep);
        if (ratio < HoursRatioFloor)
        {
            ratio = HoursRatioFloor;
        }
        var hours = Math.Round(parentHours * ratio, 0, MidpointRounding.AwayFromZero);
        return hours < MinimumAllocatedHours ? MinimumAllocatedHours : hours;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementDapiDemoSeederTests`
Expected: PASS — all 7 facts/theories across Task 2 and Task 3 green, including `SeedAsync_EveryObjective_SatisfiesParentDateAndHoursContainment` across all ~66 seeded objectives.

- [ ] **Step 5: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementDapiDemoSeeder.Objectives.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementDapiDemoSeederTests.cs
git commit -m "feat(seed): seed 5 dapi projects with 5-layer objective trees and project members"
```

---

### Task 4: Register the seeder

**Files:**
- Modify: `src/ONEVO.Infrastructure/DependencyInjection.cs:401-409`

**Interfaces:**
- Consumes: `WorkManagementDapiDemoSeeder` (Task 2/3) as an `IHostedService`.

- [ ] **Step 1: Insert the registration line**

```csharp
// src/ONEVO.Infrastructure/DependencyInjection.cs
// Background seeder
services.AddHostedService<PermissionSeeder>();
services.AddHostedService<RoleTemplateSeeder>();
services.AddHostedService<LookupDataSeeder>();
services.AddHostedService<PlatformAccessSeeder>();
services.AddHostedService<ModuleCatalogSeeder>();
services.AddHostedService<DevSmokeTestTenantSeeder>();
services.AddHostedService<WorkManagementDapiDemoSeeder>();
services.AddHostedService<PlatformOAuthProviderMetadataSeeder>();
services.AddHostedService<ProjectsAccessBootstrapSeeder>();
services.AddHostedService<WorkManagementSampleDataSeeder>();
```

(`WorkManagementDapiDemoSeeder` is inserted directly after `DevSmokeTestTenantSeeder` and before `ProjectsAccessBootstrapSeeder`/`WorkManagementSampleDataSeeder` — see the Background section's ordering rationale.)

- [ ] **Step 2: Build to verify no DI resolution errors**

Run: `dotnet build src/ONEVO.Api`
Expected: build succeeds; `WorkManagementDapiDemoSeeder`'s constructor dependencies (`IServiceProvider`, `IHostEnvironment`, `ILogger<WorkManagementDapiDemoSeeder>`) are all already registered by the generic hosting/logging setup, same as every other seeder in this list.

- [ ] **Step 3: Commit**

```bash
git add src/ONEVO.Infrastructure/DependencyInjection.cs
git commit -m "feat(seed): register WorkManagementDapiDemoSeeder in the dev seeder boot order"
```

---

### Task 5: Guard `WorkManagementSampleDataSeeder` against the dapi tenant

**Files:**
- Modify: `src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs:65-70`
- Test: `tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederDapiGuardTests.cs`

**Interfaces:**
- Consumes: nothing new — pure guard clause using the existing `Tenant.Slug` field already queried at line 65-67.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederDapiGuardTests.cs
// Seeds two active tenants (dapi via DevSmokeTestTenantSeeder, and one plain non-dapi tenant with
// its own user+employee+legal-entity created directly in the test), runs
// WorkManagementSampleDataSeeder.StartAsync-equivalent logic, and asserts the guard is tenant-
// specific: dapi gets zero SMK projects, the other tenant still gets its normal 2-per-user SMK
// projects (proving this isn't a global kill-switch).
using Microsoft.EntityFrameworkCore;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using ONEVO.Infrastructure.Persistence.Seeders;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

public class WorkManagementSampleDataSeederDapiGuardTests : IAsyncLifetime
{
    private SqliteTestApplicationDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = await SqliteTestApplicationDbContext.CreateAsync();
        await PermissionSeederTestHelper.SeedPermissionsAsync(_db);
        await LookupDataSeederTestHelper.SeedLookupsAsync(_db);
        var tenantContext = new FakeWritableTenantContext();
        var passwordHasher = new FakePasswordHasher();
        var encryption = new FakeEncryptionService();
        await DevSmokeTestTenantSeeder.SeedAsync(_db, tenantContext, passwordHasher, encryption, new FakeConfiguration(), CancellationToken.None);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RunningSampleSeeder_ProducesZeroSmkProjectsForDapiTenant()
    {
        await WorkManagementSampleDataSeederTestHarness.RunAsync(_db);

        var dapiTenantId = Guid.Parse("6b0874ab-71db-401f-859f-bdd50c1317fb");
        var smkProjectCount = await _db.Projects
            .Where(p => p.TenantId == dapiTenantId && p.Identifier.StartsWith("SMK"))
            .CountAsync();

        Assert.Equal(0, smkProjectCount);
    }
}
```

> Note: introduce a tiny internal `WorkManagementSampleDataSeederTestHarness.RunAsync(ApplicationDbContext db)` helper (mirroring `DevSmokeTestTenantSeeder`'s pattern of a public static `SeedAsync` callable from tests) if `WorkManagementSampleDataSeeder`'s logic isn't already reachable outside `StartAsync` — extract the current `StartAsync` body (lines 58-116 of the existing file) into a `public static async Task SeedAsync(ApplicationDbContext db, IWritableTenantContext tenantContext, CancellationToken ct)` static method, with `StartAsync` reduced to resolving the scope/services and delegating to it, exactly mirroring the `DevSmokeTestTenantSeeder.StartAsync` / `DevSmokeTestTenantSeeder.SeedAsync` split already used elsewhere in this codebase. This refactor is in-scope for this task since it's required to test the guard at all.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementSampleDataSeederDapiGuardTests`
Expected: FAIL — no guard exists yet, dapi tenant's owner user gets 2 SMK projects.

- [ ] **Step 3: Add the guard and the testable static entry point**

```csharp
// src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs
public sealed class WorkManagementSampleDataSeeder : IHostedService
{
    private const int TargetCategoriesPerTenant = 5;
    private const int TargetProjectsPerUser = 2;
    private const int TargetMilestonesPerProject = 2;
    private const string SampleIdentifierPrefix = "SMK";

    // WorkManagementDapiDemoSeeder is now the sole source of Work Management data for this
    // tenant (5 hand-designed projects, 22 named employees) - without this guard, every one of
    // those 22 employees would also pick up 2 generic "SMK..." sample projects here, burying the
    // curated dataset under 44 unrelated rows on every dev boot.
    private const string DapiTenantSlug = "dapi";

    private static readonly string[] CategoryNames =
        ["Backend", "Frontend", "Infrastructure", "Design", "Marketing"];

    private readonly IServiceProvider _services;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WorkManagementSampleDataSeeder> _logger;

    public WorkManagementSampleDataSeeder(
        IServiceProvider services,
        IHostEnvironment environment,
        ILogger<WorkManagementSampleDataSeeder> logger)
    {
        _services = services;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Test"))
        {
            return;
        }

        try
        {
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<IWritableTenantContext>();

            await SeedAsync(db, tenantContext, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WorkManagementSampleDataSeeder failed. Startup will stop.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static async Task SeedAsync(
        ApplicationDbContext db,
        IWritableTenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        tenantContext.SetAdminMode();
        var tenants = await db.Tenants
            .Where(t => t.Status == TenantStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var tenant in tenants)
        {
            if (tenant.Slug == DapiTenantSlug)
            {
                continue;
            }

            tenantContext.SetAdminMode();
            tenantContext.Resolve(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, PlanCode: null));

            var categories = await EnsureProjectCategoriesAsync(db, tenant.Id, cancellationToken);

            var legalEntity = await db.LegalEntities
                .FirstOrDefaultAsync(l => l.TenantId == tenant.Id && l.IsPrimary, cancellationToken);
            if (legalEntity is null)
            {
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            var users = await db.Users
                .Where(u => u.TenantId == tenant.Id && u.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                var employee = await db.Employees
                    .FirstOrDefaultAsync(e => e.TenantId == tenant.Id && e.UserId == user.Id, cancellationToken);
                if (employee is null)
                {
                    continue;
                }

                await EnsureUserSampleProjectsAsync(db, tenant.Id, user, employee, legalEntity.Id, categories, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // EnsureProjectCategoriesAsync / EnsureUserSampleProjectsAsync bodies unchanged from the
    // existing file - only StartAsync shrinks (delegates to the new SeedAsync) and the
    // `if (tenant.Slug == DapiTenantSlug) continue;` guard is new.
}
```

- [ ] **Step 4: Add the tiny test harness helper referenced by Step 1**

```csharp
// tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederTestHarness.cs
using ONEVO.Infrastructure.Persistence.Seeders;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Tenancy;

internal static class WorkManagementSampleDataSeederTestHarness
{
    public static Task RunAsync(SqliteTestApplicationDbContext db)
        => WorkManagementSampleDataSeeder.SeedAsync(db, new FakeWritableTenantContext(), CancellationToken.None);
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/ONEVO.Tests.Unit --filter FullyQualifiedName~WorkManagementSampleDataSeederDapiGuardTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/ONEVO.Infrastructure/Persistence/Seeders/WorkManagementSampleDataSeeder.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederDapiGuardTests.cs tests/ONEVO.Tests.Unit/Features/DevPlatform/Tenancy/WorkManagementSampleDataSeederTestHarness.cs
git commit -m "fix(seed): skip dapi tenant in WorkManagementSampleDataSeeder to avoid clashing with curated demo data"
```

---

### Task 6: Full regression pass

**Files:** none (verification only).

- [ ] **Step 1: Run the full unit test project**

Run: `dotnet test tests/ONEVO.Tests.Unit`
Expected: all tests pass, including the pre-existing `DevSmokeTestTenantSeederTests.cs` (unchanged — confirms this plan never touched `DevSmokeTestTenantSeeder.cs`'s fixed counts) and all new tests from Tasks 1-5.

- [ ] **Step 2: Run the architecture test project**

Run: `dotnet test tests/ONEVO.Tests.Architecture`
Expected: passes — `WorkManagementDapiDemoSeeder`/`WorkManagementDapiDemoData` are new files the existing architecture tests don't target (they only source-scan `DevSmokeTestTenantSeeder.cs` by name), and no Clean Architecture boundary is crossed (the new seeder lives entirely in `ONEVO.Infrastructure`, same layer as every other seeder).

- [ ] **Step 3: Manual boot smoke check**

Run: `dotnet run --project src/ONEVO.Api` (Development environment), watch startup logs for `Work Management dapi demo dataset seeded (22 employees, 5 projects).`, then stop and re-run once more to confirm the second boot logs the same line with no exceptions (idempotency under a real Postgres dev DB, not just SQLite).

This step is manual (no automated assertion) — report the two log lines and any exceptions back before considering the plan complete.

---

## Self-Review Notes

- **Spec coverage:** roster (22 people, 6 teams) → Task 1/2; 5 named projects → Task 1/3; 5-layer objective trees with the exact E-pos_System shape from the user's example → Task 1 data + Task 3 test; Hardware team cross-project "in charge of Hardware objective" behavior → the `Hardware Integration` branch node in `epos`/`evtix`/`onexso`/`watercraft` trees, owned by a rotating hardware-team member; Marketing team appearing in all 5 projects → the `Marketing` branch node present in every tree including `hwportal`; Dabi as top-level technical objective owner everywhere → every tree's `Root.OwnerKey == "dabi"`; dummy data for unspecified fields (email, hours) → `@dapi.test` emails and the `ComputeChildHours`/hire-date constants; onexso using real product modules → Task 1's onexso tree uses actual HR/Work-Management module names (Core HR, Employee Lifecycle, Onboarding, Payroll, Work Management Module, Auth/Security, Reporting).
- **Placeholder scan:** none found — every step above contains complete, compilable code or an exact command.
- **Type consistency:** `DemoObjectiveNode`/`DemoProjectTree`/`DemoPerson` (Task 1) are used with identical property names in Task 2/3; `employeeIdByPersonKey` is produced by `SeedPersonsAsync` (Task 2) and consumed with the same `Dictionary<string, Guid>` type by `SeedProjectsAndObjectivesAsync` (Task 3); `DeterministicGuid` is declared once (Task 2) and reused by name in Task 3 without redefinition.

---

**Plan complete and saved to `docs/superpowers/plans/next/2026-08-12-work-management-dapi-demo-data-seeding.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
