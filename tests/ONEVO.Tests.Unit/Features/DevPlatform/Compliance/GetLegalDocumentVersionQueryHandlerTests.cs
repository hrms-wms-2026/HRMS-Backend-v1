using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.GetLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class GetLegalDocumentVersionQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsDetailDto_WithContentFields()
    {
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Title = "T",
            Status = "draft", ContentHtml = "<p>Body</p>", ContentJson = "{\"type\":\"doc\"}",
            ContentText = "Body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(entity.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entity);

        var handler = new GetLegalDocumentVersionQueryHandler(repo.Object);

        var result = await handler.Handle(new GetLegalDocumentVersionQuery(entity.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHtml.Should().Be("<p>Body</p>");
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var handler = new GetLegalDocumentVersionQueryHandler(repo.Object);

        var result = await handler.Handle(new GetLegalDocumentVersionQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
