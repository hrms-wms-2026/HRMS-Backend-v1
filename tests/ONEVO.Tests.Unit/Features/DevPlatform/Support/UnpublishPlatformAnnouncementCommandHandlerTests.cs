using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.UnpublishPlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class UnpublishPlatformAnnouncementCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 13, 0, 0, TimeSpan.Zero);
    private readonly Mock<IPlatformAnnouncementRepository> _announcements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public UnpublishPlatformAnnouncementCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private UnpublishPlatformAnnouncementCommandHandler BuildSut() => new(_announcements.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_UnknownAnnouncement_ReturnsNotFound()
    {
        _announcements.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformAnnouncement?)null);

        var sut = BuildSut();
        var result = await sut.Handle(new UnpublishPlatformAnnouncementCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Handle_Unpublish_SetsIsPublishedFalse_ButKeepsPublishedAtHistory()
    {
        var publishedAt = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var announcement = new PlatformAnnouncement
        {
            Id = Guid.NewGuid(), IsPublished = true, PublishedAt = publishedAt,
        };
        _announcements.Setup(a => a.GetByIdAsync(announcement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(announcement);

        var sut = BuildSut();
        var result = await sut.Handle(new UnpublishPlatformAnnouncementCommand(announcement.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        announcement.IsPublished.Should().BeFalse();
        announcement.PublishedAt.Should().Be(publishedAt);
        announcement.UpdatedAt.Should().Be(Now);
    }
}
