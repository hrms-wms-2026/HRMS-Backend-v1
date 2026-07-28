using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;

namespace ONEVO.Infrastructure.Identity.Passwords;

public sealed class BaseLoginFixedWorkVerifier : IBaseLoginFixedWorkVerifier
{
    private const int FixedCheckCount = 8;
    private const int SupportedMaximumCandidates = 8;

    // Computed once per process, at the same BCrypt work factor as BCryptPasswordHasher, so a
    // padding check costs the same as a real one. The input string is fixed and never a real
    // credential; only IPasswordHasher.Verify's timing profile against this hash matters.
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("onevo-fixed-dummy-hash-input-do-not-use", workFactor: 12);

    private readonly IPasswordHasher _passwordHasher;

    public BaseLoginFixedWorkVerifier(IPasswordHasher passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    public Task<BaseLoginVerificationOutcome> VerifyAsync(
        IReadOnlyList<BaseLoginCandidateRow> candidates,
        string submittedPassword,
        CancellationToken ct = default)
    {
        var isOverflow = candidates.Count > SupportedMaximumCandidates;
        var candidatesToCheck = isOverflow
            ? candidates.Take(SupportedMaximumCandidates).ToList()
            : candidates.ToList();

        var matchedCandidates = new List<BaseLoginCandidateRow>();

        for (var slot = 0; slot < FixedCheckCount; slot++)
        {
            if (slot < candidatesToCheck.Count)
            {
                var candidate = candidatesToCheck[slot];
                var isMatch = _passwordHasher.Verify(submittedPassword, candidate.PasswordHash);
                if (isMatch)
                {
                    matchedCandidates.Add(candidate);
                }
            }
            else
            {
                _passwordHasher.Verify(submittedPassword, DummyPasswordHash);
            }
        }

        var outcome = isOverflow
            ? new BaseLoginVerificationOutcome([], true)
            : new BaseLoginVerificationOutcome(matchedCandidates, false);

        return Task.FromResult(outcome);
    }
}
