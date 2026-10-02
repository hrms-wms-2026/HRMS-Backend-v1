namespace ONEVO.Application.Features.WorkManagement.CalendarEvents.Services;

/// <summary>Pure done/total -> percent rollup, shared by anything that needs an Objective's (or a
/// subtree's) task-completion percentage. Null means "no tasks to measure" - render "-", never 0%.</summary>
public static class ObjectiveProgressCalculator
{
    public static int? Calculate(int doneCount, int totalCount)
        => totalCount <= 0 ? null : (int)Math.Round(100.0 * doneCount / totalCount, MidpointRounding.AwayFromZero);
}
