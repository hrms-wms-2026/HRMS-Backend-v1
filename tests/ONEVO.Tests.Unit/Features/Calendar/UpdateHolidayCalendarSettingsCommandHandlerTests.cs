using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Commands.UpdateHolidayCalendarSettings;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class UpdateHolidayCalendarSettingsCommandHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid SettingsId = Guid.NewGuid();

    private static Mock<IUnitOfWork> MakeUnitOfWork()
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return unitOfWork;
    }

    private static Mock<ICurrentUser> MakeCurrentUser()
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.UserId).Returns(UserId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        return currentUser;
    }

    [Fact]
    public async Task Handle_UpdatesOverrideCountryAndSyncFlag()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, DefaultCountryCode = "IN", HolidaySyncEnabled = true };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var sut = new UpdateHolidayCalendarSettingsCommandHandler(MakeCurrentUser().Object, settingsRepo.Object, MakeUnitOfWork().Object);

        var result = await sut.Handle(new UpdateHolidayCalendarSettingsCommand(SettingsId, "LK", false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("LK", settings.OverrideCountryCode);
        Assert.False(settings.HolidaySyncEnabled);
        Assert.Equal(UserId, settings.UpdatedById);
        settingsRepo.Verify(s => s.Update(settings), Times.Once);
    }

    [Fact]
    public async Task Handle_BlankOverride_ClearsIt()
    {
        var settings = new HolidayCalendarSettings { Id = SettingsId, TenantId = TenantId, DefaultCountryCode = "IN", OverrideCountryCode = "LK" };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var sut = new UpdateHolidayCalendarSettingsCommandHandler(MakeCurrentUser().Object, settingsRepo.Object, MakeUnitOfWork().Object);

        var result = await sut.Handle(new UpdateHolidayCalendarSettingsCommand(SettingsId, "", true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(settings.OverrideCountryCode);
    }

    [Fact]
    public async Task Handle_SettingsNotFound_ReturnsNotFound()
    {
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetTrackedByIdAsync(TenantId, SettingsId, It.IsAny<CancellationToken>())).ReturnsAsync((HolidayCalendarSettings?)null);

        var sut = new UpdateHolidayCalendarSettingsCommandHandler(MakeCurrentUser().Object, settingsRepo.Object, MakeUnitOfWork().Object);

        var result = await sut.Handle(new UpdateHolidayCalendarSettingsCommand(SettingsId, "LK", true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }
}
