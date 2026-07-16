using FluentAssertions;
using ONEVO.Application.Features.Auth.Permission;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class ModuleAutoGrantsTests
{
    [Fact]
    public void Contains_KnownAutoGrantCode_ReturnsTrue()
        => ModuleAutoGrants.Contains("leave:read-own").Should().BeTrue();

    [Fact]
    public void Contains_ExplicitPermissionCode_ReturnsFalse()
        => ModuleAutoGrants.Contains("leave:approve").Should().BeFalse();

    [Fact]
    public void Contains_Phase2Code_ReturnsFalse()
    {
        ModuleAutoGrants.Contains("payroll:read-own").Should().BeFalse();
        ModuleAutoGrants.Contains("performance:read-own").Should().BeFalse();
        ModuleAutoGrants.Contains("documents:read-own").Should().BeFalse();
    }

    [Fact]
    public void Contains_InboxOrNotifications_ReturnsFalse()
    {
        ModuleAutoGrants.Contains("inbox:read").Should().BeFalse();
        ModuleAutoGrants.Contains("notifications:read").Should().BeFalse();
    }

    [Fact]
    public void GetForModules_LeaveAndAttendance_ReturnsCorrectCodes()
    {
        var codes = ModuleAutoGrants.GetForModules(["leave", "attendance"]).ToList();

        codes.Should().Contain("leave:read-own");
        codes.Should().Contain("attendance:read-own");
        codes.Should().Contain("attendance:write-own");
        codes.Should().NotContain("employees:read-own");
        codes.Should().NotContain("inbox:read");
    }

    [Fact]
    public void GetForModules_UnknownModule_ReturnsEmpty()
        => ModuleAutoGrants.GetForModules(["payroll"]).Should().BeEmpty();

    [Fact]
    public void GetForModules_NoModules_ReturnsEmpty()
        => ModuleAutoGrants.GetForModules([]).Should().BeEmpty();

    [Fact]
    public void AllSixModulesHaveAutoGrants()
    {
        var modules = new[] { "employees", "leave", "attendance", "calendar", "monitoring", "workforce" };
        foreach (var m in modules)
            ModuleAutoGrants.GetForModules([m]).Should().NotBeEmpty(because: $"module '{m}' must have auto-grants");
    }
}
