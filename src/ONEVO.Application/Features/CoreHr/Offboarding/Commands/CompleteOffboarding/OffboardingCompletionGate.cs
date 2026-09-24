using ONEVO.Domain.Features.CoreHr.Entities;

namespace ONEVO.Application.Features.CoreHr.Offboarding.Commands.CompleteOffboarding;

/// <summary>Standalone, independently-testable completion gate - kept separate from
/// CompleteOffboardingCommandHandler's transaction so a reviewer can verify the gate logic
/// without standing up the full handler (per the design's advisor review).</summary>
public static class OffboardingCompletionGate
{
    public static bool AllRequiredTasksResolved(IReadOnlyList<EmployeeChecklistTask> tasks) =>
        tasks.Where(t => t.IsRequired)
            .All(t => t.Status is EmployeeChecklistTaskStatuses.Completed or EmployeeChecklistTaskStatuses.Bypassed);
}
