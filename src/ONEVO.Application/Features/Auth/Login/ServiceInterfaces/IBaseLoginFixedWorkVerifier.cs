using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;

namespace ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

public sealed record BaseLoginVerificationOutcome(
    IReadOnlyList<BaseLoginCandidateRow> MatchedCandidates,
    bool IsOverflow);

/// <summary>
/// Performs base-domain login password verification with fixed, bounded work: exactly eight
/// IPasswordHasher.Verify calls on every request regardless of how many real candidates exist,
/// so response timing cannot reveal the candidate count. Real candidates beyond the eighth row
/// (the overflow probe) are never checked; overflow always returns zero matches.
/// </summary>
public interface IBaseLoginFixedWorkVerifier
{
    Task<BaseLoginVerificationOutcome> VerifyAsync(
        IReadOnlyList<BaseLoginCandidateRow> candidates,
        string submittedPassword,
        CancellationToken ct = default);
}
