using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.UpdateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class UpdateLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_RecomputesContentHash_ForDraft()
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0",
            Status = "draft", Title = "Old", ContentHtml = "<p>Old</p>", ContentHash = "old-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            draft.Id, "New Title", "{}", "<p>New</p>", "New", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHash.Should().Be(LegalContentHasher.ComputeHash("<p>New</p>"));
        draft.Title.Should().Be("New Title");
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("published")]
    [InlineData("archived")]
    public async Task Handle_RejectsUpdate_WhenNotDraft(string status)
    {
        var version = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0", Status = status,
            ContentHtml = "<p>x</p>"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(version.Id, It.IsAny<CancellationToken>())).ReturnsAsync(version);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            version.Id, "New Title", "{}", "<p>New</p>", "New", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("\"just a string\"")]
    [InlineData("null")]
    public async Task Handle_RejectsInvalidContentJson(string contentJson)
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0",
            Status = "draft", Title = "Old", ContentHtml = "<p>Old</p>", ContentHash = "old-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            draft.Id, "New Title", contentJson, "<p>New</p>", "New", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_RejectsEmptyContentText(string contentText)
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0",
            Status = "draft", Title = "Old", ContentHtml = "<p>Old</p>", ContentHash = "old-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            draft.Id, "New Title", "{}", "<p>New</p>", contentText, true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("workpulse_collection")]
    [InlineData("mobile")]
    public async Task Handle_RejectsUnsupportedBlockScope(string blockScope)
    {
        var draft = new LegalDocumentVersion
        {
            Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0",
            Status = "draft", Title = "Old", ContentHtml = "<p>Old</p>", ContentHash = "old-hash"
        };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            draft.Id, "New Title", "{}", "<p>New</p>", "New", true, blockScope);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenIdMissing()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new UpdateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new UpdateLegalDocumentVersionCommand(
            Guid.NewGuid(), "Title", "{}", "<p>x</p>", "x", true, "dashboard");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
