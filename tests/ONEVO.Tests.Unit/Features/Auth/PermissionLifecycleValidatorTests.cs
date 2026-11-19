using FluentAssertions;
using ONEVO.Application.Common.Authorization;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth;

public class PermissionLifecycleValidatorTests
{
    private static readonly DateOnly Day = new(2026, 11, 19);

    private static readonly PermissionDefinition[] Catalogue =
    [
        new("a:new", "m", "d", PermissionKind.Assignable),
        new("b:live", "m", "d", PermissionKind.Assignable),
    ];

    private static IReadOnlyList<string> Validate(
        IReadOnlyList<PermissionRename>? renames = null,
        IReadOnlyList<PermissionDefaultGrant>? grants = null,
        IReadOnlyList<PermissionRetirement>? retirements = null) =>
        PermissionLifecycleValidator.Validate(Catalogue, new(renames ?? [], grants ?? [], retirements ?? []));

    [Fact]
    public void TheRealDeclarations_AreValidAgainstTheRealCatalogue()
    {
        PermissionLifecycleValidator.Validate(ONEVO.Application.Common.Authorization.PermissionCatalog.Seeded, ONEVO.Application.Common.Authorization.PermissionCatalog.Lifecycle).Should().BeEmpty();
    }

    [Fact]
    public void ValidDeclarations_PassWithoutErrors()
    {
        Validate(
            [new PermissionRename("a:old", "a:new", Day, "r")],
            [new PermissionDefaultGrant("a:new", [DefaultGrantTarget.Owner], Day, "r")],
            [new PermissionRetirement("c:gone", RetirementStage.SoftRetired, "r")]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("a:old", "a:missing", "not in the catalogue")]
    [InlineData("b:live", "a:new", "still in the catalogue")]
    [InlineData("a:new", "a:new", "points at itself")]
    public void BadRename_IsReported(string from, string to, string expected)
    {
        Validate([new PermissionRename(from, to, Day, "r")]).Should().Contain(e => e.Contains(expected));
    }

    [Fact]
    public void RenameCycle_IsReported()
    {
        Validate([new PermissionRename("x:1", "x:2", Day, "r"), new PermissionRename("x:2", "x:1", Day, "r")])
            .Should().Contain(e => e.Contains("cycle"));
    }

    [Fact]
    public void RetiringALiveOrRenamedCode_IsReported()
    {
        Validate(retirements: [new PermissionRetirement("b:live", RetirementStage.Removed, "r")])
            .Should().Contain(e => e.Contains("still in the catalogue"));
        Validate(
                [new PermissionRename("a:old", "a:new", Day, "r")],
                retirements: [new PermissionRetirement("a:old", RetirementStage.Removed, "r")])
            .Should().Contain(e => e.Contains("both renamed and retired"));
    }

    [Fact]
    public void DefaultGrantForAnUnknownCodeOrWithoutTargets_IsReported()
    {
        Validate(grants: [new PermissionDefaultGrant("z:unknown", [DefaultGrantTarget.Owner], Day, "r")])
            .Should().Contain(e => e.Contains("not in the catalogue"));
        Validate(grants: [new PermissionDefaultGrant("a:new", [], Day, "r")])
            .Should().Contain(e => e.Contains("names no target"));
        Validate(grants: [new PermissionDefaultGrant("a:new", [DefaultGrantTarget.HoldersOfCode("a:new")], Day, "r")])
            .Should().Contain(e => e.Contains("its own holders"));
    }
}
