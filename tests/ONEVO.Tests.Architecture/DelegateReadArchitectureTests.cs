using ONEVO.Application.Features.WorkManagement.Meetings.Services;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Delegate task planning (2026-11-11), anti-goals: <c>forEmployeeId</c> opens only the reads the spec lists and the three
/// planner block writes. A new request type that carries a ForEmployeeId must be added here on purpose, after checking it
/// goes through IDelegateAccess (or the meeting-delegate read) and opens no task or project write.
/// </summary>
public sealed class DelegateReadArchitectureTests
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // Meeting delegates (2026-11-10): the principal's calendar and days, read-only.
        "GetCalendarEventsQuery", "GetMyPlannerRangeQuery",
        // Delegate task planning (2026-11-11): the principal's focus, blocks, and the task form's reads.
        "GetMyPlannerFocusQuery", "CreatePlannedBlockCommand", "MovePlannedBlockCommand", "DeletePlannedBlockCommand",
        "GetTaskByIdQuery", "GetProjectByIdQuery", "GetProjectSprintsQuery",
    };

    [Fact]
    public void OnlyTheListedRequests_CarryForEmployeeId()
    {
        var carrying = typeof(IDelegateAccess).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Query", StringComparison.Ordinal) || t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Where(t => t.GetProperty("ForEmployeeId") is not null)
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(carrying.Where(name => !Allowed.Contains(name)));
        Assert.Equal(Allowed.Order(StringComparer.Ordinal), carrying.Distinct().Order(StringComparer.Ordinal));
    }
}
