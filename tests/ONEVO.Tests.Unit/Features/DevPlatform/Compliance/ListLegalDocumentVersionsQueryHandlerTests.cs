using FluentAssertions;
using Moq;
using ONEVO.Application.Features.DevPlatform.Compliance.Queries.ListLegalDocumentVersions;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class ListLegalDocumentVersionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsSummaryDtos_WithoutContentBody()
    {
        var entity = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Title = "T",
            Status = "draft", ContentHtml = "<p>secret body</p>", ContentJson = "{}",
            ContentText = "secret body", ContentHash = "hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.ListAsync("terms", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LegalDocumentVersion> { entity });

        var handler = new ListLegalDocumentVersionsQueryHandler(repo.Object);

        var result = await handler.Handle(
            new ListLegalDocumentVersionsQuery("terms", null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].ContentHash.Should().Be("hash");
    }
}
