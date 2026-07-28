using FluentAssertions;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Legal.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Legal.Services;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Auth.Legal;

public sealed class LegalAcceptanceCheckerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckAsync_IncludesContentEndpointAndContentHash_ForPendingDocuments()
    {
        var required = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.1", Title = "Terms", Status = "published",
            IsRequired = true, BlockScope = "dashboard", PublishedAt = Now.AddDays(-1),
            ContentHash = "abc123"
        };

        var versions = new Mock<ILegalDocumentVersionRepository>();
        versions.Setup(v => v.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { required });

        var acceptances = new Mock<ILegalAcceptanceRepository>();
        acceptances.Setup(a => a.GetUserAcceptancesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalAcceptanceRecord>());

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var checker = new LegalAcceptanceChecker(versions.Object, acceptances.Object, clock.Object);

        var result = await checker.CheckAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.PendingDocuments.Should().ContainSingle();
        result.PendingDocuments[0].ContentEndpoint.Should().Be("/api/v1/legal/documents/terms/1.1");
        result.PendingDocuments[0].ContentHash.Should().Be("abc123");
    }
}
