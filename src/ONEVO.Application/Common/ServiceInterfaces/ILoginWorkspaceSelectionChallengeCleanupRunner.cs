namespace ONEVO.Application.Common.ServiceInterfaces;

/// <summary>
/// Runs one cleanup pass over login_workspace_selection_challenges. Extracted from the hosted
/// BackgroundService so a single pass can be invoked directly and deterministically in tests,
/// instead of waiting for the hourly timer.
/// </summary>
public interface ILoginWorkspaceSelectionChallengeCleanupRunner
{
    Task<int> RunOnceAsync(CancellationToken ct = default);
}
