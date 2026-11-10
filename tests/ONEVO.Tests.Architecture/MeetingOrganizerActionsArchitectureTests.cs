using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Meeting delegates get the Organizer role (spec "Access"), so every organizer-only check also lets a delegate act. This
/// pins the files that make such a check. A new file in the list means a new organizer-only action: decide whether a
/// delegate may take it, and if not, have it refuse when MeetingAccess.ActingForEmployeeId is set
/// (403 meeting_principal_only), then add the file here.
/// </summary>
public sealed class MeetingOrganizerActionsArchitectureTests
{
    private static readonly string[] Reviewed =
    [
        "MeetingAccessResolver.cs",
        "MeetingActionItemService.cs",
        "MeetingAttendanceReadService.cs",
        "MeetingAttendeeService.cs",
        "MeetingBulkInviteService.cs",
        "MeetingGuard.cs",
        "MeetingReadService.cs",
        "SeriesMeetingLinkService.cs",
    ];

    [Fact]
    public void OrganizerOnlyActions_AreTheReviewedList()
    {
        var services = Path.Combine(FindRepositoryRoot(), "src", "ONEVO.Application", "Features", "WorkManagement", "Meetings", "Services");
        var found = Directory.GetFiles(services, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("MeetingRoles.Organizer", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(Reviewed.SequenceEqual(found),
            "The files with MeetingRoles.Organizer checks changed. Delegates inherit every organizer check: decide whether a "
            + "delegate may use the new one (spec \"Access\"), then update this list. Found: " + string.Join(", ", found));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "ONEVO.Api")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
