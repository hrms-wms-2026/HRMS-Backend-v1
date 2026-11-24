using System.Text.Json;
using FluentValidation.TestHelper;
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
using ONEVO.Application.Features.DevPlatform.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: platform access, roles, templates, the app catalog and tray releases.</summary>
public sealed class PlatformAdminValidatorTests
{
    private static readonly JsonElement Payload = JsonDocument.Parse("{}").RootElement;

    [Fact]
    public void Invite_AtLimits_Passes_OverLimits_Fails()
    {
        var validator = new InvitePlatformManagerCommandValidator();
        validator.TestValidate(new InvitePlatformManagerCommand(S(243) + "@example.com", S(255), Ids(100))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new InvitePlatformManagerCommand(S(256), S(256), Ids(101)));
        result.ShouldHaveValidationErrorFor(x => x.Email);
        result.ShouldHaveValidationErrorFor(x => x.FullName);
        result.ShouldHaveValidationErrorFor(x => x.RoleIds);
    }

    [Fact]
    public void AcceptInvite_BoundsTokenAndPassword()
    {
        var validator = new AcceptPlatformManagerInviteCommandValidator();
        validator.TestValidate(new AcceptPlatformManagerInviteCommand(S(512), S(128))).ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new AcceptPlatformManagerInviteCommand(S(513), S(129)));
        result.ShouldHaveValidationErrorFor(x => x.RawToken);
        result.ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void PlatformRolePermissions_BoundCountAndCode()
    {
        var validator = new UpdatePlatformRolePermissionsCommandValidator();
        validator.TestValidate(new UpdatePlatformRolePermissionsCommand(Guid.NewGuid(), Strings(1_000, 120))).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new UpdatePlatformRolePermissionsCommand(Guid.NewGuid(), Strings(1_001))).ShouldHaveValidationErrorFor(x => x.Permissions);
        validator.TestValidate(new UpdatePlatformRolePermissionsCommand(Guid.NewGuid(), [S(121)])).ShouldHaveValidationErrorFor(x => x.Permissions);
    }

    [Fact]
    public void TenantRoles_AndTemplates_BoundTheirColumns()
    {
        new AdminCreateTenantRoleCommandValidator().TestValidate(new AdminCreateTenantRoleCommand(Guid.NewGuid(), S(100), S(255), Ids(1_000)))
            .ShouldNotHaveAnyValidationErrors();
        var role = new AdminCreateTenantRoleCommandValidator().TestValidate(new AdminCreateTenantRoleCommand(Guid.NewGuid(), S(101), S(256), Ids(1_001)));
        role.ShouldHaveValidationErrorFor(x => x.Name);
        role.ShouldHaveValidationErrorFor(x => x.Description);
        role.ShouldHaveValidationErrorFor(x => x.PermissionIds);

        new ApplyRoleTemplateCommandValidator().TestValidate(new ApplyRoleTemplateCommand(Guid.NewGuid(), Guid.NewGuid(), S(101), false))
            .ShouldHaveValidationErrorFor(x => x.RoleNameOverride);

        new CreateRoleTemplateCommandValidator().TestValidate(new CreateRoleTemplateCommand("HR", null, ["leave"], ["leave:read"]))
            .ShouldNotHaveAnyValidationErrors();
        var template = new UpdateRoleTemplateCommandValidator()
            .TestValidate(new UpdateRoleTemplateCommand(Guid.NewGuid(), S(101), S(256), Strings(101), [S(51)], true));
        template.ShouldHaveValidationErrorFor(x => x.Name);
        template.ShouldHaveValidationErrorFor(x => x.Description);
        template.ShouldHaveValidationErrorFor(x => x.ModuleKeys);
        template.ShouldHaveValidationErrorFor(x => x.PermissionCodes);
    }

    [Fact]
    public void ConfigurationTemplates_BoundTheirColumns()
    {
        new CreateConfigurationTemplateCommandValidator()
            .TestValidate(new CreateConfigurationTemplateCommand(S(100), S(50), S(150), S(500), Strings(100, 100), S(50), Payload, false, Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
        var create = new CreateConfigurationTemplateCommandValidator()
            .TestValidate(new CreateConfigurationTemplateCommand(S(101), S(51), S(151), S(501), [S(101)], S(51), Payload, false, Guid.NewGuid()));
        foreach (var field in new[] { "TemplateKey", "TemplateType", "Name", "Description", "ModuleKeys", "IndustryProfileTag" })
            create.ShouldHaveValidationErrorFor(field);

        new UpdateConfigurationTemplateMetadataCommandValidator()
            .TestValidate(new UpdateConfigurationTemplateMetadataCommand(Guid.NewGuid(), null, null, null, null, null)).ShouldNotHaveAnyValidationErrors();
        new UpdateConfigurationTemplateMetadataCommandValidator()
            .TestValidate(new UpdateConfigurationTemplateMetadataCommand(Guid.NewGuid(), S(151), null, null, null, null))
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void GlobalApplication_BoundsItsColumns()
    {
        var validator = new UpdateGlobalApplicationCommandValidator();
        validator.TestValidate(new UpdateGlobalApplicationCommand(Guid.NewGuid(), S(200), S(50), S(200), true, false, Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new UpdateGlobalApplicationCommand(Guid.NewGuid(), S(201), S(51), S(201), true, false, Guid.NewGuid()));
        result.ShouldHaveValidationErrorFor(x => x.AppName);
        result.ShouldHaveValidationErrorFor(x => x.Category);
        result.ShouldHaveValidationErrorFor(x => x.Publisher);
    }

    [Fact]
    public void TrayReleases_BoundTheirColumns()
    {
        var ok = new CreateTrayReleaseRequest("1.2.3", "stable", "https://cdn/x.msi", S(64), 10, "ONEVO", "10.0.19041", null, S(10_000), true);
        new CreateTrayReleaseCommandValidator().TestValidate(new CreateTrayReleaseCommand(ok, "ci", null)).ShouldNotHaveAnyValidationErrors();

        var bad = new CreateTrayReleaseRequest(S(33), S(17), S(2_049), S(65), 10, S(201), S(33), S(33), S(10_001), true);
        var result = new CreateTrayReleaseCommandValidator().TestValidate(new CreateTrayReleaseCommand(bad, S(17), null));
        foreach (var field in new[] { "Request.Version", "Request.Channel", "Request.DownloadUrl", "Request.Sha256", "Request.Publisher",
                     "Request.MinimumWindowsVersion", "Request.MinSupportedVersion", "Request.ReleaseNotes", "Source" })
            result.ShouldHaveValidationErrorFor(field);

        new UpdateTrayReleaseCommandValidator().TestValidate(new UpdateTrayReleaseCommand(Guid.NewGuid(), new UpdateTrayReleaseRequest(null, null, null, null)))
            .ShouldNotHaveAnyValidationErrors();
        new UpdateTrayReleaseCommandValidator().TestValidate(new UpdateTrayReleaseCommand(Guid.NewGuid(), new UpdateTrayReleaseRequest(S(17), null, null, null)))
            .ShouldHaveValidationErrorFor("Request.Channel");
    }
}
