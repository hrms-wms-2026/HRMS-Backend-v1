using FluentAssertions;
using Moq;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.Helpers;
using ONEVO.Application.Features.DevPlatform.Compliance.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Compliance.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Compliance;

public sealed class CreateLegalDocumentVersionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_ComputesContentHash_ForValidDraft()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByDocumentTypeAndVersionAsync("terms", "1.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LegalDocumentVersion?)null);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.1", "ONEVO Terms and Conditions",
            "{\"type\":\"doc\"}", "<h1>Terms</h1><p>Body</p>", "Terms\nBody",
            true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ContentHash.Should().Be(LegalContentHasher.ComputeHash("<h1>Terms</h1><p>Body</p>"));
        result.Value.Status.Should().Be("draft");
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("marketing")]
    [InlineData("activity_monitoring_notice")]
    [InlineData("unknown_type")]
    public async Task Handle_RejectsUnsupportedDocumentType(string documentType)
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            documentType, "1.0", "Title", "{}", "<p>x</p>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsUnsafeHtml()
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<script>alert(1)</script>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{invalid")]
    [InlineData("\"just a string\"")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public async Task Handle_RejectsInvalidContentJson(string contentJson)
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", contentJson, "<p>x</p>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_RejectsEmptyContentText(string contentText)
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<p>x</p>", contentText, true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("workpulse_collection")]
    [InlineData("mobile")]
    [InlineData("")]
    public async Task Handle_RejectsUnsupportedBlockScope(string blockScope)
    {
        var repo = new Mock<ILegalDocumentVersionRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<p>x</p>", "x", true, blockScope, Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        repo.Verify(r => r.AddAsync(It.IsAny<LegalDocumentVersion>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RejectsDuplicateVersionForSameDocumentType()
    {
        var existing = new LegalDocumentVersion { Id = Guid.NewGuid(), DocumentType = "terms", Version = "1.0" };
        var repo = new Mock<ILegalDocumentVersionRepository>();
        repo.Setup(r => r.GetByDocumentTypeAndVersionAsync("terms", "1.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IDateTimeProvider>();

        var handler = new CreateLegalDocumentVersionCommandHandler(repo.Object, uow.Object, clock.Object);

        var command = new CreateLegalDocumentVersionCommand(
            "terms", "1.0", "Title", "{}", "<p>x</p>", "x", true, "dashboard", Guid.NewGuid());

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }
}
