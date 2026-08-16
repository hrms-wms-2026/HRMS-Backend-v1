using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.PublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class PublishPlatformAnnouncementCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 11, 0, 0, TimeSpan.Zero);
    private readonly Mock<IPlatformAnnouncementRepository> _announcements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public PublishPlatformAnnouncementCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private PublishPlatformAnnouncementCommandHandler BuildSut() => new(_announcements.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_UnknownAnnouncement_ReturnsNotFound()
    {
        _announcements.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformAnnouncement?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new PublishPlatformAnnouncementCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_FirstPublish_SetsPublishedAtAndIsPublished()
    {
        var announcement = new PlatformAnnouncement { Id = Guid.NewGuid(), IsPublished = false, PublishedAt = null };
        _announcements.Setup(a => a.GetByIdAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);

        var sut = BuildSut();
        var result = await sut.Handle(new PublishPlatformAnnouncementCommand(announcement.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        announcement.IsPublished.Should().BeTrue();
        announcement.PublishedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Handle_RepublishAfterUnpublish_KeepsOriginalPublishedAt()
    {
        var originalPublishedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(), IsPublished = false, PublishedAt = originalPublishedAt,
        };
        _announcements.Setup(a => a.GetByIdAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);

        var sut = BuildSut();
        await sut.Handle(new PublishPlatformAnnouncementCommand(announcement.Id), CancellationToken.None);

        announcement.IsPublished.Should().BeTrue();
        announcement.PublishedAt.Should().Be(originalPublishedAt);
    }
}
