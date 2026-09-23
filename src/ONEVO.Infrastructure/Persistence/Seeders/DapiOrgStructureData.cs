namespace ONEVO.Infrastructure.Persistence.Seeders;

/// <summary>
/// Development-only: pure data describing the org structure (departments, positions, reporting
/// lines) laid on top of the "dapi" smoke tenant's existing accounts - the tenant owner
/// (DevSmokeTestTenantSeeder) and the 22 named Work Management demo employees
/// (WorkManagementDapiDemoSeeder). Consumed by DapiOrgStructureSeeder. No DB access here.
/// </summary>
public sealed record DapiDepartmentDef(string Code, string Name);

public sealed record DapiPositionDef(
    string Code,
    string Name,
    string DepartmentCode,
    string? ReportsToPositionCode,
    int MaxOccupancy);

/// <summary>One of the 6 pre-existing WorkManagementDapiDemoSeeder groupings: a team lead plus
/// their direct reports, mapped onto a department + a lead position + a pooled member position.</summary>
public sealed record DapiTeamGroupDef(
    string DeptCode,
    string LeadPositionCode,
    string MemberPositionCode,
    string LeadPersonKey,
    string[] MemberPersonKeys);

public sealed record DapiNewHireDef(
    string Key,
    string FirstName,
    string LastName,
    string Email,
    string EmployeeNumber,
    DateOnly HireDate,
    DateOnly DateOfBirth,
    string Gender,
    string Phone,
    string PositionCode,
    string DepartmentCode,
    string RoleName,
    string AddressLine,
    string City,
    string EmergencyContactName,
    string EmergencyContactRelationship,
    string EmergencyContactPhone);

public static class DapiOrgStructureData
{
    public const string ExecDept = "EXEC";
    public const string HrDept = "HR";

    public static readonly IReadOnlyList<DapiDepartmentDef> Departments =
    [
        new(ExecDept, "Executive & Leadership"),
        new(HrDept, "Human Resources"),
        new("EPOS", "E-Pos Systems"),
        new("EVTIX", "Event Ticketing"),
        new("ONEXSO", "Onexso Platform"),
        new("WCRAFT", "Watercraft Engineering"),
        new("HWINT", "Hardware Integration"),
        new("MKT", "Marketing"),
    ];

    // Position.Code is capped at varchar(5) in the schema (see PositionConfiguration.cs), so
    // every code below - including the reports-to references - must stay within that limit.
    public const string CeoPosition = "CEO";
    public const string GmPosition = "GM";
    public const string HrManagerPosition = "HRM";
    public const string OpsExecPosition = "OPX";

    public static readonly IReadOnlyList<DapiPositionDef> Positions =
    [
        new(CeoPosition, "Chief Executive Officer", ExecDept, null, 1),
        new(GmPosition, "General Manager", ExecDept, CeoPosition, 1),
        new(HrManagerPosition, "HR Manager", HrDept, CeoPosition, 1),
        new(OpsExecPosition, "Operations Executive", ExecDept, GmPosition, 1),

        new("EPLD", "E-Pos Systems Team Lead", "EPOS", GmPosition, 1),
        new("EPEN", "E-Pos Systems Engineer", "EPOS", "EPLD", 3),

        new("EVLD", "Event Ticketing Team Lead", "EVTIX", GmPosition, 1),
        new("EVEN", "Event Ticketing Engineer", "EVTIX", "EVLD", 3),

        new("ONLD", "Onexso Platform Team Lead", "ONEXSO", GmPosition, 1),
        new("ONEN", "Onexso Platform Engineer", "ONEXSO", "ONLD", 3),

        new("WCLD", "Watercraft Engineering Team Lead", "WCRAFT", GmPosition, 1),
        new("WCEN", "Watercraft Engineer", "WCRAFT", "WCLD", 3),

        new("HILD", "Hardware Integration Team Lead", "HWINT", GmPosition, 1),
        new("HIEN", "Hardware Integration Engineer", "HWINT", "HILD", 2),

        new("MKLD", "Marketing Team Lead", "MKT", GmPosition, 1),
        new("MKEN", "Marketing Executive", "MKT", "MKLD", 2),
    ];

    public static readonly IReadOnlyList<DapiTeamGroupDef> TeamGroups =
    [
        new("EPOS", "EPLD", "EPEN", "mathusanth", ["tharmi", "rowsas", "nevi"]),
        new("EVTIX", "EVLD", "EVEN", "danuharan", ["thamsan", "kali", "thivshana"]),
        new("ONEXSO", "ONLD", "ONEN", "kajaa", ["thivan", "paramanathan", "prakirthan"]),
        new("WCRAFT", "WCLD", "WCEN", "abitha", ["saif", "lavanya", "kunasika"]),
        new("HWINT", "HILD", "HIEN", "nilaxan", ["kiru", "basith"]),
        new("MKT", "MKLD", "MKEN", "sutharshan", ["kavisna", "sangavi"]),
    ];

    public const string RoleHrManager = "HR Manager";
    public const string RoleGeneralManager = "General Manager";
    public const string RoleManager = "Manager";
    public const string RoleEmployee = "Employee";

    public static readonly IReadOnlyList<string> HrManagerPermissionCodes =
    [
        "org:read", "org:manage", "employees:read", "employees:write",
        "roles:read", "calendar:read", "leave:read", "leave:manage", "leave:approve"
    ];

    public static readonly IReadOnlyList<string> GeneralManagerPermissionCodes =
    [
        "org:read", "org:manage", "employees:read", "employees:read-team", "roles:read",
        "calendar:read", "leave:read", "leave:approve", "monitoring:read",
        "projects:read", "projects:access", "tasks:read", "tasks:write", "tasks:approve",
        "sprints:read", "roadmaps:read"
    ];

    public static readonly IReadOnlyList<string> ManagerPermissionCodes =
    [
        "employees:read-team", "leave:approve", "calendar:read",
        "projects:read", "projects:access", "tasks:read", "tasks:write", "tasks:approve"
    ];

    /// <summary>Employee is the baseline organizational role: no explicit permissions. Universal
    /// self-service (own profile/attendance/leave/tasks) is already granted to every authenticated
    /// user automatically by ModuleAutoGrants - see PermissionResolver - so nothing needs to be
    /// seeded here. Mirrors DefaultRoleSeeder's real-tenant "Employee" role.</summary>
    public static readonly IReadOnlyList<string> EmployeePermissionCodes = [];

    public static readonly IReadOnlyList<DapiNewHireDef> NewHires =
    [
        new(
            Key: "gm",
            FirstName: "Nadesh", LastName: "Coomaraswamy",
            Email: "nadesh.coomaraswamy@dapi.test",
            EmployeeNumber: "DAPI-0024",
            HireDate: new DateOnly(2023, 1, 1),
            DateOfBirth: new DateOnly(1985, 4, 12),
            Gender: "Male",
            Phone: "+94771234024",
            PositionCode: GmPosition,
            DepartmentCode: ExecDept,
            RoleName: RoleGeneralManager,
            AddressLine: "24 Galle Road",
            City: "Colombo",
            EmergencyContactName: "Priya Coomaraswamy",
            EmergencyContactRelationship: "Spouse",
            EmergencyContactPhone: "+94771234099"),
        new(
            Key: "hrmgr",
            FirstName: "Vithya", LastName: "Ganeshalingam",
            Email: "vithya.ganeshalingam@dapi.test",
            EmployeeNumber: "DAPI-0025",
            HireDate: new DateOnly(2023, 1, 1),
            DateOfBirth: new DateOnly(1988, 9, 3),
            Gender: "Female",
            Phone: "+94771234025",
            PositionCode: HrManagerPosition,
            DepartmentCode: HrDept,
            RoleName: RoleHrManager,
            AddressLine: "17 Duplication Road",
            City: "Colombo",
            EmergencyContactName: "Ganeshalingam Kumar",
            EmergencyContactRelationship: "Father",
            EmergencyContactPhone: "+94771234098"),
        new(
            Key: "opsexec",
            FirstName: "Roshan", LastName: "Perera",
            Email: "roshan.perera@dapi.test",
            EmployeeNumber: "DAPI-0026",
            HireDate: new DateOnly(2024, 8, 1),
            DateOfBirth: new DateOnly(1996, 11, 20),
            Gender: "Male",
            Phone: "+94771234026",
            PositionCode: OpsExecPosition,
            DepartmentCode: ExecDept,
            RoleName: RoleEmployee,
            AddressLine: "9 Havelock Road",
            City: "Colombo",
            EmergencyContactName: "Nilmini Perera",
            EmergencyContactRelationship: "Mother",
            EmergencyContactPhone: "+94771234097"),
    ];
}
