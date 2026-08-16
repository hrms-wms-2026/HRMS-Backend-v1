using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Support.Commands.CreatePlatformAnnouncement;
using ONEVO.Application.Features.DevPlatform.Support.RepositoryInterfaces;
using ONEVO.Domain.Features.DevPlatform.Support.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.Support;

public sealed class CreatePlatformAnnouncementCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);
    private readonly Mock<IPlatformAnnouncementRepository> _announcements = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IDateTimeProvider> _clock = new();

    public CreatePlatformAnnouncementCommandHandlerTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
    }

    private CreatePlatformAnnouncementCommandHandler BuildSut() => new(_announcements.Object, _uow.Object, _clock.Object);

    [Fact]
    public async Task Handle_HappyPath_CreatesUnpublishedInfoAllAnnouncement()
    {
        var sut = BuildSut();

        var result = await sut.Handle(
            new CreatePlatformAnnouncementCommand("Maintenance window", "We will be down for 1 hour.", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Severity.Should().Be(PlatformAnnouncement.SeverityInfo);
        result.Value!.Audience.Should().Be(PlatformAnnouncement.AudienceAll);
        result.Value!.IsPublished.Should().BeFalse();
        result.Value!.PublishedAt.Should().BeNull();
        _announcements.Verify(
            a => a.AddAsync(It.IsAny<PlatformAnnouncement>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyTitle_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new CreatePlatformAnnouncementCommand("  ", "Body", null, null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_UnknownSeverity_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new CreatePlatformAnnouncementCommand("Title", "Body", "not_a_severity", null), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Handle_UnknownAudience_ReturnsBadRequest()
    {
        var sut = BuildSut();
        var result = await sut.Handle(
            new CreatePlatformAnnouncementCommand("Title", "Body", null, "not_an_audience"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }
}
