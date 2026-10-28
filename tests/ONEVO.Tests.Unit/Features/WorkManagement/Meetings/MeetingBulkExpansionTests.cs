using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using Xunit;

namespace ONEVO.Tests.Unit.Features.WorkManagement.Meetings;

/// <summary>
/// D8: the preview expands the picked groups into a snapshot of individual people. The counts are what the
/// frontend shows next to the list - selected, duplicates removed, inactive excluded, already invited.
/// </summary>
public sealed class MeetingBulkExpansionTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly Guid D = Guid.NewGuid();
    private static readonly Guid E = Guid.NewGuid();

    private static HashSet<Guid> Set(params Guid[] ids) => [.. ids];

    [Fact]
    public void Expand_RemovesDuplicatesAcrossGroups()
    {
        var result = MeetingBulkExpansion.Expand(
            explicitIds: [A, B],
            groups: [[C, D], [B, E]],
            active: Set(A, B, C, D, E),
            alreadyInvited: Set());

        Assert.Equal(6, result.Selected);          // every id before dedup
        Assert.Equal(1, result.Duplicates);        // B came twice
        Assert.Equal(new[] { A, B, C, D, E }, result.People);
    }

    [Fact]
    public void Expand_ExcludesInactive_CountsThemOnce()
    {
        var result = MeetingBulkExpansion.Expand(
            explicitIds: [A, B],
            groups: [[B, C]],
            active: Set(A, C),
            alreadyInvited: Set());

        Assert.Equal(4, result.Selected);
        Assert.Equal(1, result.Duplicates);        // B repeated
        Assert.Equal(1, result.Inactive);          // B is counted once, not twice
        Assert.Equal(new[] { A, C }, result.People);
    }

    [Fact]
    public void Expand_CountsAlreadyInvited()
    {
        var result = MeetingBulkExpansion.Expand(
            explicitIds: [A, B],
            groups: [[C]],
            active: Set(A, B, C),
            alreadyInvited: Set(A, C));

        Assert.Equal(2, result.AlreadyInvited);    // A and C are on the series already
        Assert.Equal(new[] { B }, result.People);  // and are not offered again
    }

    [Fact]
    public void Expand_KeepsFirstSeenOrder()
    {
        var result = MeetingBulkExpansion.Expand(
            explicitIds: [C, A],
            groups: [[A, B], [E, C]],
            active: Set(A, B, C, D, E),
            alreadyInvited: Set());

        Assert.Equal(new[] { C, A, B, E }, result.People);
    }
}
