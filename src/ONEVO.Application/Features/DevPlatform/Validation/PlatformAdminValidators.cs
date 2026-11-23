using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.UpdateGlobalApplication;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.UpdateConfigurationTemplateMetadata;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.AcceptPlatformManagerInvite;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.CreateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.Commands.UpdateTrayRelease;
using ONEVO.Application.Features.DevPlatform.SystemConfig.TrayReleases.DTOs;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminCreateTenantRole;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.UpdateRoleTemplate;

namespace ONEVO.Application.Features.DevPlatform.Validation;

/// <summary>
/// Task 19b. Platform-admin commands: platform managers and roles, tenant roles and role templates, configuration
/// templates, the global app catalog and tray releases. Column sizes:
/// <list type="bullet">
/// <item>platform_users: email and full_name 255. platform_permissions.code and
/// platform_role_permissions.permission_code 120.</item>
/// <item>roles and role_templates: name 100, description 255. Tenant permissions.code is 50. Module keys are
/// nvarchar(100) in the module catalog; the template key lists are stored as JSON.</item>
/// <item>configuration_templates: template_key 100, template_type 50, name 150, description 500,
/// industry_profile_tag 50.</item>
/// <item>global_applications: app_name 200, category 50, publisher 200.</item>
/// <item>tray_app_releases: version, minimum_windows_version and min_supported_version 32, channel and source 16,
/// download_url 2048, sha256 64, publisher 200. release_notes is nvarchar(max); 10,000 is chosen.</item>
/// </list>
/// The invite password follows the 128-character login bound and the invite token the 512-character bound task 19
/// used for MFA challenges. List caps are request bounds well above the catalogs (a few hundred permissions). The
/// handlers keep the required checks, the password policy, email format, known codes and business rules.
/// </summary>
public static class PlatformAdminLimits
{
    public const int EmailMaxLength = InputLimits.EmailMaxLength;
    public const int FullNameMaxLength = 255;
    public const int PasswordMaxLength = 128;
    public const int InviteTokenMaxLength = 512;
    public const int MaxRoles = 100;
    public const int PlatformPermissionCodeMaxLength = 120;
    public const int TenantPermissionCodeMaxLength = 50;
    public const int MaxPermissions = 1_000;
    public const int RoleNameMaxLength = 100;
    public const int RoleDescriptionMaxLength = 255;
    public const int ModuleKeyMaxLength = 100;
    public const int MaxModules = 100;

    public const int TemplateKeyMaxLength = 100;
    public const int TemplateTypeMaxLength = 50;
    public const int TemplateNameMaxLength = 150;
    public const int TemplateDescriptionMaxLength = 500;
    public const int IndustryTagMaxLength = 50;

    public const int AppNameMaxLength = 200;
    public const int AppCategoryMaxLength = 50;
    public const int AppPublisherMaxLength = 200;

    public const int VersionMaxLength = 32;
    public const int ChannelMaxLength = 16;
    public const int SourceMaxLength = 16;
    public const int DownloadUrlMaxLength = 2_048;
    public const int Sha256MaxLength = 64;
    public const int ReleasePublisherMaxLength = 200;
    public const int ReleaseNotesMaxLength = 10_000;
}

// ---- Platform managers and platform roles ----

public sealed class InvitePlatformManagerCommandValidator : AbstractValidator<InvitePlatformManagerCommand>
{
    public InvitePlatformManagerCommandValidator()
    {
        RuleFor(x => x.Email).ValidName(PlatformAdminLimits.EmailMaxLength, required: false);
        RuleFor(x => x.FullName).ValidName(PlatformAdminLimits.FullNameMaxLength, required: false);
        RuleFor(x => x.RoleIds).ValidIdList(PlatformAdminLimits.MaxRoles);
    }
}

/// <summary>Anonymous: the invite link carries the raw token. The messages never echo the token or password.</summary>
public sealed class AcceptPlatformManagerInviteCommandValidator : AbstractValidator<AcceptPlatformManagerInviteCommand>
{
    public AcceptPlatformManagerInviteCommandValidator()
    {
        RuleFor(x => x.RawToken).ValidName(PlatformAdminLimits.InviteTokenMaxLength, required: false);
        RuleFor(x => x.Password).ValidName(PlatformAdminLimits.PasswordMaxLength, required: false);
    }
}

public sealed class UpdatePlatformRolePermissionsCommandValidator : AbstractValidator<UpdatePlatformRolePermissionsCommand>
{
    public UpdatePlatformRolePermissionsCommandValidator()
    {
        RuleFor(x => x.Permissions).ValidStringList(PlatformAdminLimits.MaxPermissions, PlatformAdminLimits.PlatformPermissionCodeMaxLength);
    }
}

// ---- Tenant roles and role templates ----

public sealed class AdminCreateTenantRoleCommandValidator : AbstractValidator<AdminCreateTenantRoleCommand>
{
    public AdminCreateTenantRoleCommandValidator()
    {
        RuleFor(x => x.Name).ValidName(PlatformAdminLimits.RoleNameMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(PlatformAdminLimits.RoleDescriptionMaxLength, required: false);
        RuleFor(x => x.PermissionIds).ValidIdList(PlatformAdminLimits.MaxPermissions);
    }
}

public sealed class ApplyRoleTemplateCommandValidator : AbstractValidator<ApplyRoleTemplateCommand>
{
    public ApplyRoleTemplateCommandValidator()
    {
        RuleFor(x => x.RoleNameOverride).ValidName(PlatformAdminLimits.RoleNameMaxLength, required: false);
    }
}

/// <summary>Create and update share the template fields.</summary>
public interface IRoleTemplateInput
{
    string Name { get; }
    string? Description { get; }
    IReadOnlyList<string> ModuleKeys { get; }
    IReadOnlyList<string> PermissionCodes { get; }
}

public abstract class RoleTemplateInputValidator<T> : AbstractValidator<T> where T : IRoleTemplateInput
{
    protected RoleTemplateInputValidator()
    {
        RuleFor(x => x.Name).ValidName(PlatformAdminLimits.RoleNameMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(PlatformAdminLimits.RoleDescriptionMaxLength, required: false);
        RuleFor(x => x.ModuleKeys).ValidStringList(PlatformAdminLimits.MaxModules, PlatformAdminLimits.ModuleKeyMaxLength);
        RuleFor(x => x.PermissionCodes).ValidStringList(PlatformAdminLimits.MaxPermissions, PlatformAdminLimits.TenantPermissionCodeMaxLength);
    }
}

public sealed class CreateRoleTemplateCommandValidator : RoleTemplateInputValidator<CreateRoleTemplateCommand> { }

public sealed class UpdateRoleTemplateCommandValidator : RoleTemplateInputValidator<UpdateRoleTemplateCommand> { }

// ---- Configuration templates ----

public sealed class CreateConfigurationTemplateCommandValidator : AbstractValidator<CreateConfigurationTemplateCommand>
{
    public CreateConfigurationTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateKey).ValidName(PlatformAdminLimits.TemplateKeyMaxLength, required: false);
        RuleFor(x => x.TemplateType).ValidName(PlatformAdminLimits.TemplateTypeMaxLength, required: false);
        RuleFor(x => x.Name).ValidName(PlatformAdminLimits.TemplateNameMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(PlatformAdminLimits.TemplateDescriptionMaxLength, required: false);
        RuleFor(x => x.ModuleKeys).ValidStringList(PlatformAdminLimits.MaxModules, PlatformAdminLimits.ModuleKeyMaxLength);
        RuleFor(x => x.IndustryProfileTag).ValidName(PlatformAdminLimits.IndustryTagMaxLength, required: false);
    }
}

public sealed class UpdateConfigurationTemplateMetadataCommandValidator : AbstractValidator<UpdateConfigurationTemplateMetadataCommand>
{
    public UpdateConfigurationTemplateMetadataCommandValidator()
    {
        RuleFor(x => x.Name).ValidName(PlatformAdminLimits.TemplateNameMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(PlatformAdminLimits.TemplateDescriptionMaxLength, required: false);
        RuleFor(x => x.ModuleKeys).ValidStringList(PlatformAdminLimits.MaxModules, PlatformAdminLimits.ModuleKeyMaxLength);
        RuleFor(x => x.IndustryProfileTag).ValidName(PlatformAdminLimits.IndustryTagMaxLength, required: false);
    }
}

// ---- Global app catalog ----

public sealed class UpdateGlobalApplicationCommandValidator : AbstractValidator<UpdateGlobalApplicationCommand>
{
    public UpdateGlobalApplicationCommandValidator()
    {
        RuleFor(x => x.AppName).ValidName(PlatformAdminLimits.AppNameMaxLength, required: false);
        RuleFor(x => x.Category).ValidName(PlatformAdminLimits.AppCategoryMaxLength, required: false);
        RuleFor(x => x.Publisher).ValidName(PlatformAdminLimits.AppPublisherMaxLength, required: false);
    }
}

// ---- Tray releases ----

public sealed class CreateTrayReleaseRequestValidator : AbstractValidator<CreateTrayReleaseRequest>
{
    public CreateTrayReleaseRequestValidator()
    {
        RuleFor(x => x.Version).ValidName(PlatformAdminLimits.VersionMaxLength, required: false);
        RuleFor(x => x.Channel).ValidName(PlatformAdminLimits.ChannelMaxLength, required: false);
        RuleFor(x => x.DownloadUrl).ValidName(PlatformAdminLimits.DownloadUrlMaxLength, required: false);
        RuleFor(x => x.Sha256).ValidName(PlatformAdminLimits.Sha256MaxLength, required: false);
        RuleFor(x => x.Publisher).ValidName(PlatformAdminLimits.ReleasePublisherMaxLength, required: false);
        RuleFor(x => x.MinimumWindowsVersion).ValidName(PlatformAdminLimits.VersionMaxLength, required: false);
        RuleFor(x => x.MinSupportedVersion).ValidName(PlatformAdminLimits.VersionMaxLength, required: false);
        RuleFor(x => x.ReleaseNotes).ValidName(PlatformAdminLimits.ReleaseNotesMaxLength, required: false);
    }
}

public sealed class CreateTrayReleaseCommandValidator : AbstractValidator<CreateTrayReleaseCommand>
{
    public CreateTrayReleaseCommandValidator()
    {
        RuleFor(x => x.Request).SetValidator(new CreateTrayReleaseRequestValidator());
        RuleFor(x => x.Source).ValidName(PlatformAdminLimits.SourceMaxLength, required: false);
    }
}

public sealed class UpdateTrayReleaseCommandValidator : AbstractValidator<UpdateTrayReleaseCommand>
{
    public UpdateTrayReleaseCommandValidator()
    {
        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.Channel).ValidName(PlatformAdminLimits.ChannelMaxLength, required: false);
            RuleFor(x => x.Request.MinSupportedVersion).ValidName(PlatformAdminLimits.VersionMaxLength, required: false);
            RuleFor(x => x.Request.ReleaseNotes).ValidName(PlatformAdminLimits.ReleaseNotesMaxLength, required: false);
        });
    }
}
