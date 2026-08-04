using System.Reflection;
using ONEVO.Api.Contracts.OrgStructure.LegalEntities;
using ONEVO.Application.Features.OrgStructure.Commands.CreateLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.DeleteLegalEntity;
using ONEVO.Application.Features.OrgStructure.Commands.RemoveLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.SetLegalEntityLogo;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.GetLegalEntityGeneralSettings;
using ONEVO.Application.Features.OrgStructure.Queries.ListLegalEntities;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the Part 2B application-layer scope for Legal Entity / Company
/// General Settings: no contract or command accepts TenantId from the
/// caller, no controller/route was added yet, mutating handlers fetch
/// before they mutate, and delete is soft (no physical row removal).
/// </summary>
public class LegalEntityPart2BArchitectureTests
{
    private static readonly Type[] RequestContractTypes =
    [
        typeof(CreateLegalEntityRequest),
        typeof(UpdateLegalEntityGeneralSettingsRequest),
        typeof(DeleteLegalEntityRequest),
        typeof(SetLegalEntityLogoRequest)
    ];

    private static readonly Type[] CommandAndQueryTypes =
    [
        typeof(CreateLegalEntityCommand),
        typeof(UpdateLegalEntityGeneralSettingsCommand),
        typeof(DeleteLegalEntityCommand),
        typeof(SetLegalEntityLogoCommand),
        typeof(RemoveLegalEntityLogoCommand),
        typeof(ListLegalEntitiesQuery),
        typeof(GetLegalEntityGeneralSettingsQuery)
    ];

    [Theory]
    [MemberData(nameof(AllRequestContractTypes))]
    public void RequestContracts_DoNotExposeTenantId(Type contractType)
    {
        var hasTenantId = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.Name == "TenantId");

        Assert.False(hasTenantId, $"{contractType.Name} must not expose a TenantId property.");
    }

    [Theory]
    [MemberData(nameof(AllCommandAndQueryTypes))]
    public void CommandsAndQueries_DoNotAcceptTenantId(Type requestType)
    {
        var hasTenantId = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.Name == "TenantId");

        Assert.False(hasTenantId, $"{requestType.Name} must not accept TenantId - it must come from ICurrentUser.");
    }

    public static IEnumerable<object[]> AllRequestContractTypes()
        => RequestContractTypes.Select(t => new object[] { t });

    public static IEnumerable<object[]> AllCommandAndQueryTypes()
        => CommandAndQueryTypes.Select(t => new object[] { t });

    // NoControllerOrRoute_AddedForLegalEntitiesYet was removed here: it asserted
    // no LegalEntities controller existed, which was correct for Part 2B's scope
    // but is now obsolete now that Part 2C legitimately adds
    // Controllers/Tenant/OrgStructure/LegalEntitiesController.cs. Controller/route
    // and permission-mapping rules for that controller now live in
    // LegalEntitiesControllerArchitectureTests.cs.

    [Fact]
    public void DeleteLegalEntityHandler_NeverPhysicallyRemovesTheRow()
    {
        var source = ReadSource("DeleteLegalEntity", "DeleteLegalEntityCommandHandler.cs");

        Assert.DoesNotContain(".Remove(", source);
        Assert.DoesNotContain(".RemoveRange(", source);
        Assert.DoesNotContain("DELETE FROM", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsActive = false", source);
    }

    [Fact]
    public void UpdateLegalEntityHandler_FetchesExistingEntityBeforeMutatingOrSaving()
    {
        var source = ReadSource("UpdateLegalEntityGeneralSettings", "UpdateLegalEntityGeneralSettingsCommandHandler.cs");

        var fetchIndex = source.IndexOf("GetByIdForTenantAsync", StringComparison.Ordinal);
        var updateIndex = source.IndexOf("_legalEntities.Update(", StringComparison.Ordinal);
        var saveIndex = source.IndexOf("SaveChangesAsync", StringComparison.Ordinal);

        Assert.True(fetchIndex >= 0, "expected a GetByIdForTenantAsync fetch call");
        Assert.True(updateIndex > fetchIndex, "Update() must be called after the entity is fetched, not before");
        Assert.True(saveIndex > updateIndex, "SaveChangesAsync must follow Update(), not precede it");

        // Guards against the "construct a detached entity and call Update()"
        // anti-pattern the Part 2A report explicitly warned against - the
        // handler must never `new LegalEntity` before mutating.
        Assert.DoesNotContain("new LegalEntity", source);
    }

    [Theory]
    [InlineData("CreateLegalEntity", "CreateLegalEntityCommand.cs")]
    [InlineData("CreateLegalEntity", "CreateLegalEntityCommandHandler.cs")]
    [InlineData("CreateLegalEntity", "CreateLegalEntityCommandValidator.cs")]
    [InlineData("UpdateLegalEntityGeneralSettings", "UpdateLegalEntityGeneralSettingsCommand.cs")]
    [InlineData("UpdateLegalEntityGeneralSettings", "UpdateLegalEntityGeneralSettingsCommandHandler.cs")]
    [InlineData("UpdateLegalEntityGeneralSettings", "UpdateLegalEntityGeneralSettingsCommandValidator.cs")]
    [InlineData("DeleteLegalEntity", "DeleteLegalEntityCommand.cs")]
    [InlineData("DeleteLegalEntity", "DeleteLegalEntityCommandHandler.cs")]
    [InlineData("DeleteLegalEntity", "DeleteLegalEntityCommandValidator.cs")]
    [InlineData("SetLegalEntityLogo", "SetLegalEntityLogoCommand.cs")]
    [InlineData("SetLegalEntityLogo", "SetLegalEntityLogoCommandHandler.cs")]
    [InlineData("RemoveLegalEntityLogo", "RemoveLegalEntityLogoCommand.cs")]
    [InlineData("RemoveLegalEntityLogo", "RemoveLegalEntityLogoCommandHandler.cs")]
    public void CommandFiles_LiveUnderOrgStructureLegalEntity_NotDevPlatformTenancy(string subfolder, string fileName)
    {
        var commandsRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "LegalEntity", "Commands");
        var path = Path.Combine(commandsRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/LegalEntity/Commands");

        var devPlatformPath = path
            .Replace(
                Path.Combine("OrgStructure", "LegalEntity"),
                Path.Combine("DevPlatform", "Tenancy"));
        Assert.False(File.Exists(devPlatformPath), "Part 2B must not add files under DevPlatform/Tenancy");
    }

    [Theory]
    [InlineData("ListLegalEntities", "ListLegalEntitiesQuery.cs")]
    [InlineData("ListLegalEntities", "ListLegalEntitiesQueryHandler.cs")]
    [InlineData("GetLegalEntityGeneralSettings", "GetLegalEntityGeneralSettingsQuery.cs")]
    [InlineData("GetLegalEntityGeneralSettings", "GetLegalEntityGeneralSettingsQueryHandler.cs")]
    public void QueryFiles_LiveUnderOrgStructureLegalEntity(string subfolder, string fileName)
    {
        var queriesRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "LegalEntity", "Queries");
        var path = Path.Combine(queriesRoot, subfolder, fileName);

        Assert.True(File.Exists(path), $"expected {path} to exist under OrgStructure/LegalEntity/Queries");
    }

    [Fact]
    public void ApiContracts_LiveUnderOrgStructureLegalEntities()
    {
        var contractsDir = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Api", "Contracts", "OrgStructure", "LegalEntities");

        var expectedFiles = new[]
        {
            "CreateLegalEntityRequest.cs",
            "UpdateLegalEntityGeneralSettingsRequest.cs",
            "DeleteLegalEntityRequest.cs",
            "SetLegalEntityLogoRequest.cs"
        };

        foreach (var file in expectedFiles)
            Assert.True(File.Exists(Path.Combine(contractsDir, file)), $"expected {file} under Contracts/OrgStructure/LegalEntities");
    }

    private static string ReadSource(params string[] relativeSegments)
    {
        var handlersRoot = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "LegalEntity", "Commands");
        var path = Path.Combine([handlersRoot, .. relativeSegments]);
        Assert.True(File.Exists(path), $"expected {path} to exist");
        return File.ReadAllText(path);
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
