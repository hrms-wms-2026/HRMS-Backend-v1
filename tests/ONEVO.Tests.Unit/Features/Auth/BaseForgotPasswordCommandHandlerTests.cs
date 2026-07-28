using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Login.Commands.BaseForgotPassword;
using ONEVO.Application.Features.Auth.Login.OutboxHandlers;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.ServiceInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Tests.Unit.Features.Auth;

public sealed class BaseForgotPasswordCommandHandlerTests
{
    private sealed class HandlerFixture
    {
        public Mock<IBaseLoginCandidateRepository> Candidates { get; } = new();
        public Mock<IPasswordResetTokenRepository> PasswordResetTokens { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<ISecureTokenGenerator> TokenService { get; } = new();
        public Mock<IOutboxWriter> Outbox { get; } = new();
        public Mock<IDateTimeProvider> Clock { get; } = new();
        public Mock<ITenantContextSwitcher> TenantSwitcher { get; } = new();
        public Mock<ILogger<BaseForgotPasswordCommandHandler>> Logger { get; } = new();

        /// <summary>
        /// Records the order operations happen in, tagged with the tenant they ran against, so
        /// tests can prove each candidate's switch happens before that same candidate's writes -
        /// not just that a switch happened somewhere.
        /// </summary>
        public List<string> CallOrder { get; } = new();

        public HandlerFixture()
        {
            Clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.Parse("2026-07-27T12:00:00Z"));

            TenantSwitcher
                .Setup(s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()))
                .Callback<TenantRegistryEntry, CancellationToken>((t, _) => CallOrder.Add($"Switch:{t.TenantId}"))
                .Returns(Task.CompletedTask);

            PasswordResetTokens
                .Setup(r => r.ListValidByUserIdAsync(
                    It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
                .Callback<Guid, DateTimeOffset, CancellationToken>((userId, _, _) => CallOrder.Add($"List:{userId}"))
                .ReturnsAsync(Array.Empty<PasswordResetToken>());

            PasswordResetTokens
                .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
                .Callback<PasswordResetToken, CancellationToken>((t, _) => CallOrder.Add($"Add:{t.TenantId}"))
                .Returns(Task.CompletedTask);

            var tokenCounter = 0;
            TokenService
                .Setup(t => t.GenerateOpaqueToken())
                .Returns(() => $"raw-token-{++tokenCounter}");
            TokenService
                .Setup(t => t.HashToken(It.IsAny<string>()))
                .Returns((string raw) => $"hash-of-{raw}");

            Outbox
                .Setup(o => o.EnqueueAsync(
                    It.IsAny<string>(),
                    It.IsAny<PasswordResetEmailPayload>(),
                    It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<string, PasswordResetEmailPayload, Guid?, CancellationToken>(
                    (_, payload, _, _) => CallOrder.Add($"Enqueue:{payload.TenantId}"))
                .Returns(Task.CompletedTask);

            UnitOfWork
                .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => CallOrder.Add("Save"))
                .ReturnsAsync(1);
        }

        public BaseForgotPasswordCommandHandler Build() => new(
            Candidates.Object,
            PasswordResetTokens.Object,
            UnitOfWork.Object,
            TokenService.Object,
            Outbox.Object,
            Clock.Object,
            TenantSwitcher.Object,
            Logger.Object);

        public void VerifyEnqueued(Guid tenantId, Guid userId, string email, string rawToken, string slug) =>
            Outbox.Verify(
                o => o.EnqueueAsync(
                    OutboxMessageTypes.PasswordResetEmail,
                    It.Is<PasswordResetEmailPayload>(p =>
                        p.TenantId == tenantId
                        && p.UserId == userId
                        && p.Email == email
                        && p.RawToken == rawToken
                        && p.TenantSlug == slug),
                    tenantId,
                    It.IsAny<CancellationToken>()),
                Times.Once);

        public void VerifyNeverEnqueued() =>
            Outbox.Verify(
                o => o.EnqueueAsync(
                    It.IsAny<string>(), It.IsAny<PasswordResetEmailPayload>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
                Times.Never);

        public void VerifySwitchedTo(Guid tenantId, string slug) =>
            TenantSwitcher.Verify(
                s => s.SwitchToTenantAsync(
                    It.Is<TenantRegistryEntry>(t => t.TenantId == tenantId && t.Slug == slug),
                    It.IsAny<CancellationToken>()),
                Times.Once);

        public void VerifyNeverSwitched() =>
            TenantSwitcher.Verify(
                s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()),
                Times.Never);
    }

    [Fact]
    public async Task Handle_WithNoEligibleCandidates_CreatesNoTokenSendsNoEmailReturnsSuccess()
    {
        var fixture = new HandlerFixture();
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("unknown@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<BaseLoginCandidateRow>());

        var result = await fixture.Build().Handle(
            new BaseForgotPasswordCommand("unknown@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.VerifyNeverSwitched();
        fixture.PasswordResetTokens.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNeverEnqueued();
    }

    [Fact]
    public async Task Handle_WithOneEligibleCandidate_SwitchesTenantThenCreatesOneTokenAndEnqueuesOneTenantBoundEmailThenSavesOnce()
    {
        var fixture = new HandlerFixture();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var candidate = new BaseLoginCandidateRow(tenantId, userId, "acme", "Acme Test", "irrelevant-hash");
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("owner@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidate });

        PasswordResetToken? added = null;
        fixture.PasswordResetTokens
            .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
            .Callback<PasswordResetToken, CancellationToken>((t, _) =>
            {
                added = t;
                fixture.CallOrder.Add($"Add:{t.TenantId}");
            })
            .Returns(Task.CompletedTask);

        var result = await fixture.Build().Handle(
            new BaseForgotPasswordCommand(" Owner@Acme.Test "), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        added.Should().NotBeNull();
        added!.TenantId.Should().Be(tenantId);
        added.UserId.Should().Be(userId);

        fixture.VerifySwitchedTo(tenantId, "acme");
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.VerifyEnqueued(tenantId, userId, "owner@acme.test", "raw-token-1", "acme");

        fixture.CallOrder.Should().Equal(
            $"Switch:{tenantId}", $"List:{userId}", $"Add:{tenantId}", $"Enqueue:{tenantId}", "Save");
    }

    [Fact]
    public async Task Handle_WithMultipleEligibleCandidates_SwitchesAndSavesOncePerTenant_NoWorkspaceNamesInResult()
    {
        var fixture = new HandlerFixture();
        var candidateA = new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "hash-a");
        var candidateB = new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "globex", "Globex Test", "hash-b");
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("shared@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidateA, candidateB });

        var addedTokens = new List<PasswordResetToken>();
        fixture.PasswordResetTokens
            .Setup(r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
            .Callback<PasswordResetToken, CancellationToken>((t, _) =>
            {
                addedTokens.Add(t);
                fixture.CallOrder.Add($"Add:{t.TenantId}");
            })
            .Returns(Task.CompletedTask);

        var result = await fixture.Build().Handle(
            new BaseForgotPasswordCommand("shared@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Result carries no tenant/workspace data at all - only Result.Success(), same shape
        // whichever branch ran, so the controller's generic response can never leak candidate count.
        result.Should().BeOfType<ONEVO.Application.Common.Models.Result>();

        addedTokens.Should().HaveCount(2);
        addedTokens.Select(t => t.TenantId).Should().BeEquivalentTo(new[] { candidateA.TenantId, candidateB.TenantId });

        fixture.VerifySwitchedTo(candidateA.TenantId, "acme");
        fixture.VerifySwitchedTo(candidateB.TenantId, "globex");
        fixture.TenantSwitcher.Verify(
            s => s.SwitchToTenantAsync(It.IsAny<TenantRegistryEntry>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Each tenant is a fully independent switch -> writes -> save unit: no cross-tenant
        // SaveChangesAsync batching. SaveChangesAsync must be called once per candidate (twice
        // total here), not once for the whole request.
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

        fixture.VerifyEnqueued(candidateA.TenantId, candidateA.UserId, "shared@example.com", "raw-token-1", "acme");
        fixture.VerifyEnqueued(candidateB.TenantId, candidateB.UserId, "shared@example.com", "raw-token-2", "globex");

        fixture.CallOrder.Should().Equal(
            $"Switch:{candidateA.TenantId}", $"List:{candidateA.UserId}", $"Add:{candidateA.TenantId}",
            $"Enqueue:{candidateA.TenantId}", "Save",
            $"Switch:{candidateB.TenantId}", $"List:{candidateB.UserId}", $"Add:{candidateB.TenantId}",
            $"Enqueue:{candidateB.TenantId}", "Save");
    }

    [Fact]
    public async Task Handle_InvalidatesExistingValidTokensForEachCandidateBeforeIssuingNewOnes()
    {
        var fixture = new HandlerFixture();
        var candidate = new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), "acme", "Acme Test", "hash");
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("owner@acme.test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { candidate });

        var staleToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = candidate.UserId,
            TenantId = candidate.TenantId,
            TokenHash = "stale-hash",
            ExpiresAt = DateTimeOffset.Parse("2026-07-27T13:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-27T11:00:00Z")
        };
        fixture.PasswordResetTokens
            .Setup(r => r.ListValidByUserIdAsync(candidate.UserId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTimeOffset, CancellationToken>((userId, _, _) => fixture.CallOrder.Add($"List:{userId}"))
            .ReturnsAsync(new[] { staleToken });

        await fixture.Build().Handle(new BaseForgotPasswordCommand("owner@acme.test"), CancellationToken.None);

        staleToken.UsedAt.Should().Be(DateTimeOffset.Parse("2026-07-27T12:00:00Z"));

        // The tenant switch must land before the candidate's existing tokens are even read -
        // ListValidByUserIdAsync is itself a tenant-owned-table read gated by RLS.
        fixture.CallOrder.IndexOf($"Switch:{candidate.TenantId}")
            .Should().BeLessThan(fixture.CallOrder.IndexOf($"List:{candidate.UserId}"));
    }

    [Fact]
    public async Task Handle_WithNineCandidates_TreatsAsOverflow_NeverSwitchesTenantOrWrites_ButReturnsSuccess()
    {
        var fixture = new HandlerFixture();
        var candidates = Enumerable.Range(0, 9)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync("overflow@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        var result = await fixture.Build().Handle(
            new BaseForgotPasswordCommand("overflow@example.com"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.VerifyNeverSwitched();
        fixture.PasswordResetTokens.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.PasswordResetTokens.Verify(
            r => r.ListValidByUserIdAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "overflow must short-circuit before touching any per-candidate existing tokens");
        fixture.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        fixture.VerifyNeverEnqueued();
    }

    [Fact]
    public async Task Handle_WithOverflow_LogsSafeWarningWithoutPlaintextEmail()
    {
        var fixture = new HandlerFixture();
        const string plaintextEmail = "overflow-target@example.com";
        var candidates = Enumerable.Range(0, 9)
            .Select(i => new BaseLoginCandidateRow(Guid.NewGuid(), Guid.NewGuid(), $"tenant-{i}", $"Tenant {i}", $"hash-{i}"))
            .ToArray();
        fixture.Candidates
            .Setup(r => r.GetCandidatesAsync(plaintextEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        await fixture.Build().Handle(new BaseForgotPasswordCommand(plaintextEmail), CancellationToken.None);

        var loggedMessages = fixture.Logger.Invocations
            .Where(i => i.Method.Name == "Log")
            .Select(i => i.Arguments[2]?.ToString() ?? string.Empty)
            .ToList();

        loggedMessages.Should().NotBeEmpty("the overflow path must log a warning");
        loggedMessages.Should().OnlyContain(
            m => !m.Contains(plaintextEmail, StringComparison.OrdinalIgnoreCase),
            "overflow warning logs must never contain the plaintext email");
    }
}
