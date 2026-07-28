using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetPublishedLegalDocument;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetPublishedLegalDocumentQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDocument_WhenPublished()
    {
        var entity = new LegalDocumentVersion
        {
            DocumentType = "terms", Version = "1.0", Title = "T", Status = "published",
            ContentHtml = "<p>Body</p>", ContentText = "Body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetPublishedAsync("terms", "1.0", It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var handler = new GetPublishedLegalDocumentQueryHandler(repo.Object);

        var result = await handler.Handle(
            new GetPublishedLegalDocumentQuery("terms", "1.0"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHtml.Should().Be("<p>Body</p>");
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenDraftOrMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetPublishedAsync("terms", "0.9-draft", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var handler = new GetPublishedLegalDocumentQueryHandler(repo.Object);

        var result = await handler.Handle(
            new GetPublishedLegalDocumentQuery("terms", "0.9-draft"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
