using FluentAssertions;
using ONEVO.Infrastructure.Services.Monitoring.ActivityMonitoring;

namespace ONEVO.Tests.Unit.Features.Monitoring.ActivityMonitoring;

/// <summary>
/// Pure resolution-chain tests (no DB). Infrastructure service composes this chain
/// after loading employee/policy/tenant rows.
/// </summary>
public class MonitoringToggleResolverTests
{
    [Fact]
    public void No_toggle_row_returns_false()
    {
        MonitoringToggleResolution.Resolve(null, null, null, null, null)
            .Should().BeFalse();
    }

    [Fact]
    public void Tenant_toggle_fallback()
    {
        MonitoringToggleResolution.Resolve(null, null, null, null, tenantToggle: true)
            .Should().BeTrue();

        MonitoringToggleResolution.Resolve(null, null, null, null, tenantToggle: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Employee_override_wins_over_policy_and_tenant()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: false,
                rolePolicy: true,
                positionPolicy: true,
                departmentPolicy: true,
                tenantToggle: true)
            .Should().BeFalse();

        MonitoringToggleResolution.Resolve(
                employeeOverride: true,
                rolePolicy: false,
                positionPolicy: false,
                departmentPolicy: false,
                tenantToggle: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Policy_override_wins_over_tenant_toggle()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                rolePolicy: true,
                positionPolicy: null,
                departmentPolicy: null,
                tenantToggle: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Role_wins_over_position_and_department()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                rolePolicy: false,
                positionPolicy: true,
                departmentPolicy: true,
                tenantToggle: true)
            .Should().BeFalse();
    }

    [Fact]
    public void Position_wins_over_department()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                rolePolicy: null,
                positionPolicy: true,
                departmentPolicy: false,
                tenantToggle: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Department_wins_over_tenant()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                rolePolicy: null,
                positionPolicy: null,
                departmentPolicy: true,
                tenantToggle: false)
            .Should().BeTrue();
    }
}
