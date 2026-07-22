using ONEVO.Application.Features.Storage.Quota.Helpers;
using ONEVO.Domain.Features.SharedPlatform.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Storage;

public class ModuleStorageAllowanceCalculatorTests
{
    private const long Gb = 1024L * 1024L * 1024L;

    private static ModuleCatalogItem Module(
        string key,
        string storageReference = "[]",
        bool isStorageConsuming = true) =>
        new()
        {
            ModuleKey = key,
            IsStorageConsuming = isStorageConsuming,
            IsActive = true,
            StorageReference = storageReference
        };

    [Fact]
    public void MatchesBracketByCompanySizeRange()
    {
        var catalog = new[]
        {
            Module("core_hr",
                """[{"min_employees":1,"max_employees":50,"storage_gb":25},{"min_employees":51,"max_employees":200,"storage_gb":100}]""")
        };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "51-200");

        Assert.Equal(100 * Gb, bytes);
    }

    [Fact]
    public void MatchesOpenEndedCompanySizeRange()
    {
        var catalog = new[]
        {
            Module("core_hr",
                """[{"min_employees":501,"max_employees":-1,"storage_gb":500}]""")
        };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "501+");

        Assert.Equal(500 * Gb, bytes);
    }

    [Fact]
    public void SumsContributionsAcrossModules()
    {
        var catalog = new[]
        {
            Module("core_hr", """[{"min_employees":1,"max_employees":50,"storage_gb":25}]"""),
            Module("work_management", """[{"min_employees":1,"max_employees":50,"storage_gb":10}]""")
        };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(
            catalog, ["core_hr", "work_management"], "1-50");

        Assert.Equal(35 * Gb, bytes);
    }

    [Fact]
    public void NonStorageConsumingModule_ContributesZero()
    {
        var catalog = new[]
        {
            Module("integrations",
                """[{"min_employees":1,"max_employees":50,"storage_gb":25}]""",
                isStorageConsuming: false)
        };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["integrations"], "1-50");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void EmptyStorageReference_ContributesZero()
    {
        var catalog = new[] { Module("core_hr", "[]") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "1-50");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void MalformedStorageReference_ContributesZero_FailSafe()
    {
        var catalog = new[] { Module("core_hr", "{not valid json") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "1-50");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void UnknownModuleKey_IsIgnored()
    {
        var catalog = new[] { Module("core_hr", """[{"min_employees":1,"max_employees":50,"storage_gb":25}]""") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["does_not_exist"], "1-50");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void NoMatchingBracket_ContributesZero()
    {
        var catalog = new[] { Module("core_hr", """[{"min_employees":1,"max_employees":50,"storage_gb":25}]""") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "201-500");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void InvalidCompanySizeRange_ContributesZero()
    {
        var catalog = new[] { Module("core_hr", """[{"min_employees":1,"max_employees":50,"storage_gb":25}]""") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, ["core_hr"], "not-a-range");

        Assert.Equal(0, bytes);
    }

    [Fact]
    public void NoSelectedModules_ContributesZero()
    {
        var catalog = new[] { Module("core_hr", """[{"min_employees":1,"max_employees":50,"storage_gb":25}]""") };

        var bytes = ModuleStorageAllowanceCalculator.CalculateTotalBytes(catalog, [], "1-50");

        Assert.Equal(0, bytes);
    }
}
