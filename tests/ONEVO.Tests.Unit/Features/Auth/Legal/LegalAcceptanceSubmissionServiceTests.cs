using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.Commands.SubmitLegalAcceptance;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public sealed class LegalAcceptanceSubmissionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static LegalDocumentVersion BuildCurrentTerms(string contentHash)
    {
        return new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.1", Status = "published",
            IsRequired = true, BlockScope = "dashboard", PublishedAt = Now.AddDays(-1),
            ContentHash = contentHash
        };
    }

    [Fact]
    public async Task ValidateAndStageAsync_RejectsMismatchedClientSuppliedContentHash()
    {
        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { BuildCurrentTerms("server-hash") });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var service = new LegalAcceptanceSubmissionService(versions.Object, acceptances.Object, clock.Object);

        var items = new List<LegalAcceptanceItemInput>
        {
            new("terms", "1.1", "accepted", ContentHash: "client-supplied-wrong-hash")
        };

        var result = await service.ValidateAndStageAsync(
            Guid.NewGuid(), Guid.NewGuid(), items, requireComplete: false, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ValidateAndStageAsync_Accepts_WhenContentHashOmitted()
    {
        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { BuildCurrentTerms("server-hash") });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var service = new LegalAcceptanceSubmissionService(versions.Object, acceptances.Object, clock.Object);

        var items = new List<LegalAcceptanceItemInput> { new("terms", "1.1", "accepted") };

        var result = await service.ValidateAndStageAsync(
            Guid.NewGuid(), Guid.NewGuid(), items, requireComplete: false, null, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
