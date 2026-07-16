namespace ONEVO.Application.Features.DevPlatform.Provisioning;

public static class TenantSetupOptionKeys
{
    public const string JobFamilyCreation = "job_family_creation";
    public const string EmployeeInvite = "employee_invite";
    public const string RolePermissionConfiguration = "role_permission_configuration";

    public static readonly IReadOnlyList<string> All =
    [
        JobFamilyCreation,
        EmployeeInvite,
        RolePermissionConfiguration
    ];
}
