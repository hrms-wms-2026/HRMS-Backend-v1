using FluentAssertions;
using Moq;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Infrastructure.Identity.Passwords;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class BaseLoginFixedWorkVerifierTests
{
    [Fact]
    public async Task VerifyAsync_WithZeroCandidates_PerformsExactlyEightVerifyCalls()
    {
        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var verifier = new BaseLoginFixedWorkVerifier(passwordHasherMock.Object);

        var outcome = await verifier.VerifyAsync(Array.Empty<BaseLoginCandidateRow>(), "SubmittedPassword1!");

        passwordHasherMock.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(8));
        outcome.MatchedCandidates.Should().BeEmpty();
        outcome.IsOverflow.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithOneMatchingCandidateAmongThree_PerformsExactlyEightVerifyCalls_AndReturnsOnlyTheMatch()
    {
        var candidates = new[]
        {
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "hash-a"),
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "beta", "Beta Test", "hash-b"),
            new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "gamma", "Gamma Test", "hash-c")
        };
        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock.Setup(h => h.Verify("SubmittedPassword1!", "hash-a")).Returns(false);
        passwordHasherMock.Setup(h => h.Verify("SubmittedPassword1!", "hash-b")).Returns(true);
        passwordHasherMock.Setup(h => h.Verify("SubmittedPassword1!", "hash-c")).Returns(false);
        passwordHasherMock
            .Setup(h => h.Verify("SubmittedPassword1!", It.Is<string>(hash => hash != "hash-a" && hash != "hash-b" && hash != "hash-c")))
            .Returns(false);
        var verifier = new BaseLoginFixedWorkVerifier(passwordHasherMock.Object);

        var outcome = await verifier.VerifyAsync(candidates, "SubmittedPassword1!");

        passwordHasherMock.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(8));
        outcome.MatchedCandidates.Should().ContainSingle(c => c.Slug == "beta");
        outcome.IsOverflow.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithEightCandidates_PerformsExactlyEightVerifyCalls_AllReal()
    {
        var candidates = Enumerable.Range(0, 8)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        var passwordHasherMock = new Mock<IPasswordHasher>();
        passwordHasherMock.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var verifier = new BaseLoginFixedWorkVerifier(passwordHasherMock.Object);

        await verifier.VerifyAsync(candidates, "SubmittedPassword1!");

        foreach (var candidate in candidates)
        {
            passwordHasherMock.Verify(h => h.Verify("SubmittedPassword1!", candidate.PasswordHash), Times.Once);
        }
        passwordHasherMock.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(8));
    }

    [Fact]
    public async Task VerifyAsync_WithNineCandidates_IsOverflow_ChecksOnlyFirstEight_AndReturnsNoMatches()
    {
        var candidates = Enumerable.Range(0, 9)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        var passwordHasherMock = new Mock<IPasswordHasher>();
        // Even the ninth (overflow-probe) candidate's real hash would match, proving overflow
        // discards any match rather than disclosing it.
        passwordHasherMock.Setup(h => h.Verify("SubmittedPassword1!", "hash-8")).Returns(true);
        passwordHasherMock
            .Setup(h => h.Verify("SubmittedPassword1!", It.Is<string>(hash => hash != "hash-8")))
            .Returns(false);
        var verifier = new BaseLoginFixedWorkVerifier(passwordHasherMock.Object);

        var outcome = await verifier.VerifyAsync(candidates, "SubmittedPassword1!");

        outcome.IsOverflow.Should().BeTrue();
        outcome.MatchedCandidates.Should().BeEmpty("overflow must disclose no workspace/count data regardless of any match");
        passwordHasherMock.Verify(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(8));
        passwordHasherMock.Verify(h => h.Verify("SubmittedPassword1!", "hash-8"), Times.Never,
            "the ninth row is the overflow probe and must never be BCrypt-checked");
    }

    [Fact]
    public async Task VerifyAsync_UsesTheSameDummyHashValue_ForEveryPaddingSlot()
    {
        var passwordHasherMock = new Mock<IPasswordHasher>();
        var observedDummyHashes = new List<string>();
        passwordHasherMock
            .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((_, hash) => observedDummyHashes.Add(hash))
            .Returns(false);
        var verifier = new BaseLoginFixedWorkVerifier(passwordHasherMock.Object);

        await verifier.VerifyAsync(Array.Empty<BaseLoginCandidateRow>(), "SubmittedPassword1!");

        observedDummyHashes.Should().HaveCount(8);
        observedDummyHashes.Distinct().Should().ContainSingle("all eight padding checks must use the same configured dummy hash");
    }
}
