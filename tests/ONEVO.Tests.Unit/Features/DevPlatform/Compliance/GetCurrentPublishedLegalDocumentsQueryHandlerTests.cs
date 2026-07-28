using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetCurrentPublishedLegalDocuments;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetCurrentPublishedLegalDocumentsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsOnlyCurrentRequiredDashboardDocuments()
    {
        var terms = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.0", Title = "Terms", Status = "published",
            ContentHtml = "<p>T</p>", ContentText = "T", ContentHash = "terms-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetCurrentRequiredVersionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { terms });

        var handler = new GetCurrentPublishedLegalDocumentsQueryHandler(repo.Object);

        var result = await handler.Handle(new GetCurrentPublishedLegalDocumentsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ContentHash.Should().Be("terms-hash");
    }
}
