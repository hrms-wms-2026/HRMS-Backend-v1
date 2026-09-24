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
        MonitoringToggleResolution.Resolve(null, null, null, null, null, null)
            .Should().BeFalse();
    }

    [Fact]
    public void Tenant_toggle_fallback()
    {
        MonitoringToggleResolution.Resolve(null, null, null, null, null, tenantToggle: true)
            .Should().BeTrue();

        MonitoringToggleResolution.Resolve(null, null, null, null, null, tenantToggle: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Employee_override_wins_over_policy_and_tenant()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: false,
                workModeOverride: true,
                rolePolicy: true,
                positionPolicy: true,
                departmentPolicy: true,
                tenantToggle: true)
            .Should().BeFalse();

        MonitoringToggleResolution.Resolve(
                employeeOverride: true,
                workModeOverride: false,
                rolePolicy: false,
                positionPolicy: false,
                departmentPolicy: false,
                tenantToggle: false)
            .Should().BeTrue();
    }

    [Fact]
    public void WorkMode_override_wins_over_role_position_department_and_tenant()
    {
        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                workModeOverride: false,
                rolePolicy: true,
                positionPolicy: true,
                departmentPolicy: true,
                tenantToggle: true)
            .Should().BeFalse();

        MonitoringToggleResolution.Resolve(
                employeeOverride: null,
                workModeOverride: true,
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
                workModeOverride: null,
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
                workModeOverride: null,
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
                workModeOverride: null,
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
                workModeOverride: null,
                rolePolicy: null,
                positionPolicy: null,
                departmentPolicy: true,
                tenantToggle: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Minutes_No_override_anywhere_returns_default_two()
    {
        MonitoringToggleResolution.ResolveMinutes(null, null, null, null, null, null)
            .Should().Be(2);
    }

    [Fact]
    public void Minutes_Tenant_toggle_fallback()
    {
        MonitoringToggleResolution.ResolveMinutes(null, null, null, null, null, tenantMinutes: 10)
            .Should().Be(10);
    }

    [Fact]
    public void Minutes_Employee_override_wins_over_everything()
    {
        MonitoringToggleResolution.ResolveMinutes(
                employeeMinutes: 2,
                workModeMinutes: 15,
                roleMinutes: 15,
                positionMinutes: 15,
                departmentMinutes: 15,
                tenantMinutes: 15)
            .Should().Be(2);
    }

    [Fact]
    public void Minutes_WorkMode_wins_over_role_position_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveMinutes(
                employeeMinutes: null,
                workModeMinutes: 6,
                roleMinutes: 15,
                positionMinutes: 15,
                departmentMinutes: 15,
                tenantMinutes: 15)
            .Should().Be(6);
    }

    [Fact]
    public void Minutes_Role_wins_over_position_and_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveMinutes(
                employeeMinutes: null,
                workModeMinutes: null,
                roleMinutes: 7,
                positionMinutes: 20,
                departmentMinutes: 20,
                tenantMinutes: 20)
            .Should().Be(7);
    }

    [Fact]
    public void Minutes_Position_wins_over_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveMinutes(
                employeeMinutes: null,
                workModeMinutes: null,
                roleMinutes: null,
                positionMinutes: 8,
                departmentMinutes: 20,
                tenantMinutes: 20)
            .Should().Be(8);
    }

    [Fact]
    public void Minutes_Department_wins_over_tenant()
    {
        MonitoringToggleResolution.ResolveMinutes(
                employeeMinutes: null,
                workModeMinutes: null,
                roleMinutes: null,
                positionMinutes: null,
                departmentMinutes: 9,
                tenantMinutes: 20)
            .Should().Be(9);
    }

    [Fact]
    public void Radius_No_override_anywhere_returns_null()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(null, null, null, null, null, null)
            .Should().BeNull();
    }

    [Fact]
    public void Radius_Tenant_default_fallback()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(null, null, null, null, null, tenantRadius: 500)
            .Should().Be(500);
    }

    [Fact]
    public void Radius_Employee_override_wins_over_everything()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(
                employeeRadius: 50,
                workModeRadius: 150,
                roleRadius: 200,
                positionRadius: 200,
                departmentRadius: 200,
                tenantRadius: 500)
            .Should().Be(50);
    }

    [Fact]
    public void Radius_WorkMode_wins_over_role_position_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(
                employeeRadius: null,
                workModeRadius: 150,
                roleRadius: 200,
                positionRadius: 200,
                departmentRadius: 200,
                tenantRadius: 500)
            .Should().Be(150);
    }

    [Fact]
    public void Radius_Role_wins_over_position_and_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(
                employeeRadius: null,
                workModeRadius: null,
                roleRadius: 250,
                positionRadius: 300,
                departmentRadius: 300,
                tenantRadius: 500)
            .Should().Be(250);
    }

    [Fact]
    public void Radius_Position_wins_over_department_and_tenant()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(
                employeeRadius: null,
                workModeRadius: null,
                roleRadius: null,
                positionRadius: 300,
                departmentRadius: 400,
                tenantRadius: 500)
            .Should().Be(300);
    }

    [Fact]
    public void Radius_Department_wins_over_tenant()
    {
        MonitoringToggleResolution.ResolveRadiusMeters(
                employeeRadius: null,
                workModeRadius: null,
                roleRadius: null,
                positionRadius: null,
                departmentRadius: 400,
                tenantRadius: 500)
            .Should().Be(400);
    }
}
