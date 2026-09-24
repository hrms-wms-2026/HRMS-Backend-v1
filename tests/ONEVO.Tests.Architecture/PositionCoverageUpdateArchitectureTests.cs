using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using ONEVO.Api.Contracts.OrgStructure.Positions;
using ONEVO.Application.Features.OrgStructure.Commands.UpdateManualCoverageRecord;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the manual-coverage "edit level" endpoint added to update a coverage record's ownerOrder
/// without allowing target/source/lock/status to be re-pointed through the same request.
/// </summary>
public class PositionCoverageUpdateArchitectureTests
{
    private static readonly string[] ForbiddenUpdateRequestPropertyNames =
    [
        "TenantId", "LegalEntityId", "Source", "IsLocked", "Status",
        "CoveredTargetType", "CoveredPositionId", "CoveredDepartmentId", "OwnerPositionId"
    ];

    [Fact]
    public void UpdateCoverageRecordRequest_OnlyExposesOwnerOrder()
    {
        var properties = typeof(UpdateCoverageRecordRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var names = properties.Select(p => p.Name).ToList();
        Assert.Equal(["OwnerOrder", "ResponsibleEmployeeId"], names);
    }

    [Fact]
    public void UpdateCoverageRecordRequest_DoesNotExposeForbiddenFields()
    {
        var offending = typeof(UpdateCoverageRecordRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => ForbiddenUpdateRequestPropertyNames.Any(forbidden =>
                p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(offending.Count == 0, $"UpdateCoverageRecordRequest must not expose: {string.Join(", ", offending)}");
    }

    [Fact]
    public void UpdateManualCoverageRecordCommand_DoesNotExposeForbiddenFields()
    {
        var offending = typeof(UpdateManualCoverageRecordCommand)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "LegalEntityId" && p.Name != "PositionId"
                && ForbiddenUpdateRequestPropertyNames.Any(forbidden =>
                    p.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.True(offending.Count == 0, $"UpdateManualCoverageRecordCommand must not expose: {string.Join(", ", offending)}");
    }

    [Fact]
    public void PositionsController_ExposesPutCoverageEndpoint()
    {
        var method = typeof(ONEVO.Api.Controllers.Tenant.OrgStructure.PositionsController)
            .GetMethod("UpdateCoverage", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);

        var httpPut = method!.GetCustomAttribute<HttpPutAttribute>();
        Assert.NotNull(httpPut);
        Assert.Equal("{positionId:guid}/coverage/{coverageId:guid}", httpPut!.Template);
    }

    [Fact]
    public void UpdateManualCoverageRecordCommandHandler_ChecksLockedAndSourceBeforeMutating()
    {
        var handlerPath = FindDirectoryUnderRepoRoot(
            "src", "ONEVO.Application", "Features", "OrgStructure", "Position",
            "Commands", "UpdateManualCoverageRecord", "UpdateManualCoverageRecordCommandHandler.cs");

        var text = File.ReadAllText(handlerPath);

        Assert.Contains("record.IsLocked", text);
        Assert.Contains("SourceManual", text);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", text);
    }

    private static string FindDirectoryUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
