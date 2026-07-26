using System.Reflection;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the retirement of the stale hardcoded tenant setup-option model
/// (TenantSetupOptionKeys, job_family_creation, role_permission_configuration,
/// and active tenant_setup_selections repository usage) documented in
/// SETUP_SERVICES_TEMPLATE_MODEL_RECONCILIATION_REPORT.md. The canonical Phase 1
/// model is setup_services / tenant_setup_services (not yet implemented in this
/// backend) and configuration_templates / tenant_configuration_template_applications.
/// The tenant_setup_selections and tenant_one_time_charges tables/migrations are
/// intentionally left in place (legacy/inactive) - this suite only guards against
/// the retired code paths being reintroduced.
/// </summary>
public class SetupOptionModelRetirementArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(ONEVO.Application.Common.Models.Result).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(ONEVO.Infrastructure.Persistence.ApplicationDbContext).Assembly;

    [Fact]
    public void TenantSetupOptionKeys_NoLongerExists()
    {
        var type = ApplicationAssembly.GetType(
            "ONEVO.Application.Features.DevPlatform.Provisioning.TenantSetupOptionKeys");
        Assert.True(type is null,
            "TenantSetupOptionKeys (job_family_creation/employee_invite/role_permission_configuration) is stale and must not be reintroduced.");
    }

    [Fact]
    public void ITenantSetupSelectionRepository_NoLongerExists()
    {
        var type = ApplicationAssembly.GetType(
            "ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces.ITenantSetupSelectionRepository");
        Assert.True(type is null,
            "ITenantSetupSelectionRepository is retired; tenant_setup_selections has no canonical OneVo-HR support.");
    }

    [Fact]
    public void EfTenantSetupSelectionRepository_NoLongerExists()
    {
        var type = InfrastructureAssembly.GetType(
            "ONEVO.Infrastructure.Persistence.Repositories.DevPlatform.Provisioning.EfTenantSetupSelectionRepository");
        Assert.True(type is null,
            "EfTenantSetupSelectionRepository is retired; tenant_setup_selections has no active writer.");
    }

    [Fact]
    public void CreateTenantCommandHandler_DoesNotDependOnTenantSetupSelectionRepository()
    {
        var handlerType = typeof(ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant.CreateTenantCommandHandler);
        var ctor = handlerType.GetConstructors().Single();
        var offender = ctor.GetParameters()
            .FirstOrDefault(p => p.ParameterType.Name == "ITenantSetupSelectionRepository");
        Assert.True(offender is null,
            "CreateTenantCommandHandler must not depend on ITenantSetupSelectionRepository (stale tenant_setup_selections model).");
    }

    [Fact]
    public void CreateTenantRequest_And_CreateTenantCommand_HaveNoSetupOptionsProperty()
    {
        var requestType = typeof(ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests.CreateTenantRequest);
        var commandType = typeof(ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant.CreateTenantCommand);

        foreach (var type in new[] { requestType, commandType })
        {
            var offender = type.GetProperties()
                .FirstOrDefault(p => p.Name is "TenantConfigurationSetup" or "SetupOptions");
            Assert.True(offender is null,
                $"{type.Name} must not expose a setup_options-shaped property (stale tenant setup-option model).");
        }
    }

    [Fact]
    public void SourceTree_HasNoActiveReferencesToRetiredSetupOptionIdentifiers()
    {
        var srcDir = FindSrcDirectory();
        var banned = new[]
        {
            "TenantSetupOptionKeys",
            "job_family_creation",
            "role_permission_configuration",
            "ITenantSetupSelectionRepository",
            "EfTenantSetupSelectionRepository"
        };

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            // Migrations are historical schema snapshots, not active code paths.
            if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file);
            foreach (var term in banned)
            {
                if (text.Contains(term, StringComparison.Ordinal))
                    offenders.Add($"{file}: {term}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Retired setup-option identifiers found in active source: " + string.Join("; ", offenders));
    }

    private static string FindSrcDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src");
            if (Directory.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src directory above " + AppContext.BaseDirectory);
    }
}
