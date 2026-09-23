using ONEVO.Application.Features.Leave.Policy.RepositoryInterfaces;

namespace ONEVO.Application.Features.Leave.Policy.Helpers;

public static class LeavePolicyConflictMessages
{
    public static string BuildReplacementConflictMessage(IReadOnlyList<LeavePolicyLegalEntityConflict> conflicts)
    {
        if (conflicts.Count == 1)
            return $"Legal Entity {conflicts[0].LegalEntityName} already has an active policy. Activating this policy will replace it. Continue?";

        var names = string.Join(", ", conflicts.Select(c => c.LegalEntityName));
        return $"Legal entities already have active policies: {names}. Activating this policy will replace them. Continue?";
    }
}
