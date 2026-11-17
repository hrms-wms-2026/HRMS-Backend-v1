using FluentAssertions;
using ONEVO.Application.Features.SharedPlatform.ModuleCatalog.Helpers;
using Xunit;

namespace ONEVO.Tests.Unit.Features.SharedPlatform;

public class ModuleKeyAliasesTests
{
    [Theory]
    [InlineData("org_structure", "org")]
    [InlineData("leave", "time_off")]
    [InlineData("activity_monitoring", "monitoring")]
    [InlineData("desktop_agent_gateway", "monitoring")]
    [InlineData("discrepancy_engine", "monitoring")]
    [InlineData("exception_engine", "monitoring")]
    [InlineData("identity_verification", "verification")]
    [InlineData("productivity_analytics", "analytics")]
    [InlineData("worksync_foundation", "work_management")]
    [InlineData("projects", "work_management")]
    [InlineData("objectives_milestones", "work_management")]
    [InlineData("tasks", "work_management")]
    [InlineData("boards", "work_management")]
    [InlineData("planning_sprints", "work_management")]
    [InlineData("attendance", "time_attendance")]
    [InlineData("employees", "core_hr")]
    [InlineData("core_hr", "core_hr")]
    [InlineData("work_management", "work_management")]
    [InlineData("unknown_module", "unknown_module")]
    public void Canonicalize_maps_legacy_and_keeps_canonical(string input, string expected)
        => ModuleKeyAliases.Canonicalize(input).Should().Be(expected);

    [Fact]
    public void Canonicalize_is_case_sensitive_like_module_keys()
        => ModuleKeyAliases.Canonicalize("Tasks").Should().Be("Tasks");

    [Fact]
    public void CanonicalizeAll_trims_dedupes_keeps_first_order_and_drops_blanks()
        => ModuleKeyAliases.CanonicalizeAll([" tasks", "projects", null, "", "  ", "core_hr", "work_management", "leave"])
            .Should().Equal("work_management", "core_hr", "time_off");

    [Fact]
    public void ExpandWithLegacyAliases_adds_every_legacy_key_for_each_canonical_key()
        => ModuleKeyAliases.ExpandWithLegacyAliases(["org"])
            .Should().BeEquivalentTo(["org", "org_structure"]);

    [Fact]
    public void ExpandWithLegacyAliases_expands_work_management_to_all_six_worksync_keys()
        => ModuleKeyAliases.ExpandWithLegacyAliases(["work_management", "core_hr"])
            .Should().BeEquivalentTo([
                "work_management", "worksync_foundation", "projects", "objectives_milestones",
                "tasks", "boards", "planning_sprints", "core_hr", "employees"]);
}
