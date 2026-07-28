using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.PublishLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class PublishLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    private static IUnitOfWork BuildPassthroughUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<bool>>, CancellationToken>((op, ct) => op(ct));
        return uow.Object;
    }

    [Fact]
    public async Task Handle_ArchivesPreviousPublished_AndPublishesNewDraft()
    {
        var oldPublished = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "published",
            ContentHtml = "<p>Old</p>", ContentText = "Old", ContentJson = "{}"
        };
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.1", Status = "draft",
            ContentHtml = "<p>New</p>", ContentText = "New", ContentJson = "{}"
        };

        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        repo.Setup(r => r.GetCurrentPublishedByDocumentTypeAsync("terms", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldPublished);

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var actorId = Guid.NewGuid();
        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), clock.Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, "Initial baseline", actorId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        oldPublished.Status.Should().Be("archived");
        draft.Status.Should().Be("published");
        draft.PublishedAt.Should().Be(Now);
        draft.PublishedById.Should().Be(actorId);
        draft.PublishReason.Should().Be("Initial baseline");
    }

    [Fact]
    public async Task Handle_Publishes_WhenNoPriorPublishedVersionExists()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft",
            ContentHtml = "<p>New</p>", ContentText = "New", ContentJson = "{}"
        };

        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);
        repo.Setup(r => r.GetCurrentPublishedByDocumentTypeAsync("terms", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), clock.Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        draft.Status.Should().Be("published");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Handle_RejectsPublish_WhenNotDraft(string status)
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = status,
            ContentHtml = "<p>x</p>", ContentText = "x", ContentJson = "{}"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(version.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Handle_RejectsPublish_WhenContentIsEmpty()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = "draft",
            ContentHtml = "", ContentText = "", ContentJson = ""
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var handler = new PublishLegalDocumentVersionCommandHandler(
            repo.Object, BuildPassthroughUnitOfWork(), new Mock<IDateTimeProvider>().Object);

        var result = await handler.Handle(
            new PublishLegalDocumentVersionCommand(draft.Id, null, Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
