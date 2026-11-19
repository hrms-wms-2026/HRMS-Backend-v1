using ONEVO.Domain.Features.CoreHr.Entities;
using ONEVO.Application.Features.CoreHr.Onboarding.RepositoryInterfaces;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

/// <summary>Standalone, independently-testable completion gate - kept separate from
/// CompleteOffboardingCommandHandler's transaction so a reviewer can verify the gate logic
/// without standing up the full handler (per the design's advisor review).</summary>
public static class OffboardingCompletionGate
{
    public static bool AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTask> tasks) =>
        tasks.Where(task => task.IsRequired)
            .All(task => task.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed);

    public static bool AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTaskEffectiveState> tasks) =>
        tasks.Where(row => row.Task.IsRequired)
            .All(row => row.IsCompleted || row.Task.Status == EmployeeChecklistTaskStatuses.Bypassed);
}
