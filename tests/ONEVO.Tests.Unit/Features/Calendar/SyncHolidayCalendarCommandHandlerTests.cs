using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.SyncHolidayCalendar;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class SyncHolidayCalendarCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SettingsId = Guid.NewGuid();

    private static Mock<IUnitOfWork> MakeUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task<int>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<int>> op, CancellationToken ct) => op(ct));
        return unitOfWork;
    }

    [Fact]
    public async Task Handle_SyncsAndRecordsLastSyncedYear()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, DefaultCountryCode = "IN", HolidaySyncEnabled = true };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var client = new Mock<INagerHolidaysClient>();
        client.Setup(c => c.GetPublicHolidaysAsync("IN", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NagerHoliday(new DateOnly(2026, 1, 26), "Republic Day", true)]);
        var events = new Mock<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.UserId).Returns(UserId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var sut = new SyncHolidayCalendarCommandHandler(currentUser.Object, settingsRepo.Object, client.Object, events.Object, MakeUnitOfWork().Object);

        var result = await sut.Handle(new SyncHolidayCalendarCommand(SettingsId, 2026), CancellationToken.None);

        Assert.True(result.IsSuccess);
        events.Verify(e => e.RemoveHolidayEventsForYearAsync(TenantId, 2026, It.IsAny<CancellationToken>()), Times.Once);
        events.Verify(e => e.AddAsync(
            It.Is<ONEVO.Domain.Features.Calendar.Entities.CalendarEvent>(ev => ev.Title == "Republic Day" && ev.SourceType == CalendarEventSourceTypes.Holiday),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2026, settings.LastSyncedYear);
    }

    [Fact]
    public async Task Handle_SyncDisabled_ReturnsFailureWithoutCallingProvider()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, HolidaySyncEnabled = false };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        var client = new Mock<INagerHolidaysClient>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var sut = new SyncHolidayCalendarCommandHandler(currentUser.Object, settingsRepo.Object, client.Object,
            Mock.Of<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>(), MakeUnitOfWork().Object);

        var result = await sut.Handle(new SyncHolidayCalendarCommand(SettingsId, 2026), CancellationToken.None);

        Assert.False(result.IsSuccess);
        client.Verify(c => c.GetPublicHolidaysAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SettingsNotFound_ReturnsNotFound()
    {
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync((HolidayCalendarSettings?)null);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);

        var sut = new SyncHolidayCalendarCommandHandler(currentUser.Object, settingsRepo.Object, Mock.Of<INagerHolidaysClient>(),
            Mock.Of<ONEVO.Application.Features.Calendar.RepositoryInterfaces.ICalendarEventRepository>(), MakeUnitOfWork().Object);

        var result = await sut.Handle(new SyncHolidayCalendarCommand(SettingsId, 2026), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
