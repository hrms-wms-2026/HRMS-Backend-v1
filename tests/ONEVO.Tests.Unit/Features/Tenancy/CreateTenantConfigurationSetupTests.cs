using System.Reflection;
using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;

namespace ONEVO.Tests.Unit.Features.Tenancy;

/// <summary>
/// Proves the stale tenant_configuration_setup.setup_options contract has been
/// removed from tenant creation. Canonical setup services are selected after
/// tenant creation (PUT /admin/v1/tenants/{id}/setup-services), not embedded in
/// POST /admin/v1/tenants — see SETUP_SERVICES_TEMPLATE_MODEL_RECONCILIATION_REPORT.md.
/// </summary>
public class CreateTenantSetupOptionsContractTests
{
    [Fact]
    public void CreateTenantRequest_HasNoSetupOptionsOrConfigurationSetupProperty()
    {
        var propertyNames = typeof(CreateTenantRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        propertyNames.Should().NotContain("TenantConfigurationSetup");
        propertyNames.Should().NotContain("SetupOptions");
    }

    [Fact]
    public void CreateTenantCommand_HasNoSetupOptionsOrConfigurationSetupProperty()
    {
        var propertyNames = typeof(CreateTenantCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        propertyNames.Should().NotContain("TenantConfigurationSetup");
        propertyNames.Should().NotContain("SetupOptions");
    }
}
