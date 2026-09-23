using System.Reflection;
using ONEVO.Application.Features.CoreHr.PositionAssignment.Models;
using ONEVO.Application.Features.OrgStructure.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Guards the occupant-preview contract added to position list/tree responses: no raw
/// file_records.storage_key ever reaches the response DTOs, and the response/model shapes stay
/// exactly what the contract promises.
/// </summary>
public class PositionOccupantPreviewArchitectureTests
{
    [Theory]
    [InlineData(typeof(PositionOccupantPreviewResponse))]
    [InlineData(typeof(PositionListItemResponse))]
    [InlineData(typeof(PositionTreeNodeResponse))]
    [InlineData(typeof(PositionOccupancyPreview))]
    [InlineData(typeof(PositionOccupantPreviewItem))]
    public void OccupantPreviewTypes_NeverExposeStorageKey(Type type)
    {
        var propertyNames = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain(propertyNames, name => name.Contains("StorageKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PositionOccupantPreviewResponse_HasExactlyTheContractShape()
    {
        var propertyNames = typeof(PositionOccupantPreviewResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string> { "EmployeeId", "DisplayName", "Initials", "AvatarFileId", "AvatarUrl" },
            propertyNames);
    }

    [Fact]
    public void PositionListItemResponse_AndPositionTreeNodeResponse_ExposeOccupantPreviewFields()
    {
        string[] expected = ["AssignedCount", "OccupantPreview", "RemainingAssignedCount", "MaxOccupancy"];

        var listProperties = typeof(PositionListItemResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
        var treeProperties = typeof(PositionTreeNodeResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();

        foreach (var name in expected)
        {
            Assert.Contains(name, listProperties);
            Assert.Contains(name, treeProperties);
        }
    }

    // Capacity enforcement and the occupant preview must use the same "is this a seat" rule
    // (active + planned PrimaryEmployment assignments only - see
    // IPositionAssignmentRepository.TryReservePositionAssignmentAsync). Both handlers below are
    // the only max_occupancy enforcement call sites for onboarding invitations; this guards
    // against either one growing a second, divergent way of counting/reserving assignments (e.g.
    // querying PositionAssignments directly, or a non-atomic count-then-insert) instead of going
    // through the single shared atomic reservation method. Updated 2026-08-16 (multi-legal-entity
    // employment foundation, part-1): capacity enforcement moved from a non-atomic
    // CountActiveAsync-then-AddAsync pair (a real race between two concurrent invites for the
    // last vacancy) to one atomic INSERT-guarded-by-capacity-subquery call - the assertion below
    // was re-pointed at the new method name, not weakened.
    [Theory]
    [InlineData(
        "src", "ONEVO.Application", "Features", "CoreHr", "OnboardingDraft", "Services",
        "OnboardingDraftWriteService.cs")]
    [InlineData(
        "src", "ONEVO.Application", "Features", "CoreHr", "Onboarding", "Commands",
        "ApproveAccessGrantRequest", "ApproveAccessGrantRequestCommandHandler.cs")]
    public void CapacityEnforcingHandler_OnlyReservesAssignmentsThrough_TryReservePositionAssignmentAsync(params string[] relativeSegments)
    {
        var path = FindFileUnderRepoRoot(relativeSegments);
        var text = File.ReadAllText(path);

        Assert.Contains("_positionAssignmentRepository.TryReservePositionAssignmentAsync(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PositionAssignments", text, StringComparison.Ordinal);
    }

    private static string FindFileUnderRepoRoot(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, .. relativeSegments]);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate " + Path.Combine(relativeSegments) + " above " + AppContext.BaseDirectory);
    }
}
