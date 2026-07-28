using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.ArchiveLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class ArchiveLegalDocumentVersionCommandHandlerTests
{
    [Fact]
    public async Task Handle_ArchivesPublishedVersion_WithoutMutatingContent()
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "published",
            ContentHtml = "<p>Keep me</p>", ContentText = "Keep me", ContentJson = "{}", ContentHash = "keep-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        var handler = new ArchiveLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var result = await handler.Handle(new ArchiveLegalDocumentVersionCommand(version.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        version.Status.Should().Be("archived");
        version.ContentHtml.Should().Be("<p>Keep me</p>");
        version.ContentHash.Should().Be("keep-hash");
    }

    [Fact]
    public async Task Handle_RejectsArchive_WhenNotPublished()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var handler = new ArchiveLegalDocumentVersionCommandHandler(
            repo.Object, new Mock<IUnitOfWork>().Object, new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(new ArchiveLegalDocumentVersionCommand(draft.Id), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
