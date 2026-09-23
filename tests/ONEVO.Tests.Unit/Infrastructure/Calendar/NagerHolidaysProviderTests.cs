using Moq;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Calendar;

public sealed class NagerHolidaysProviderTests
{
    [Fact]
    public async Task ListHolidaysAsync_SyncDisabled_ReturnsEmptyWithoutCallingProvider()
    {
        var client = new Mock<INagerHolidaysClient>();
        var settings = new Mock<IHolidayCalendarSettingsRepository>();
        var legalEntityId = Guid.NewGuid();
        settings.Setup(s => s.GetByLegalEntityAsync(It.IsAny<Guid>(), legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolidayCalendarSettings { DefaultCountryCode = "IN", HolidaySyncEnabled = false });
        var sut = new NagerHolidaysProvider(client.Object, settings.Object);

        var result = await sut.ListHolidaysAsync(Guid.NewGuid(), [legalEntityId], new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(result);
        client.Verify(c => c.GetPublicHolidaysAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ListHolidaysAsync_EmptyLegalEntities_ReturnsEmpty()
    {
        var sut = new NagerHolidaysProvider(Mock.Of<INagerHolidaysClient>(), Mock.Of<IHolidayCalendarSettingsRepository>());

        var result = await sut.ListHolidaysAsync(Guid.NewGuid(), Array.Empty<Guid>(), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31));

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListHolidaysAsync_CalendarOverload_ReturnsHolidaysAcrossLegalEntities()
    {
        var client = new Mock<INagerHolidaysClient>();
        client.Setup(c => c.GetPublicHolidaysAsync("LK", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NagerHoliday(new DateOnly(2026, 2, 4), "Independence Day", true)]);
        var legalEntityId = Guid.NewGuid();
        var settings = new Mock<IHolidayCalendarSettingsRepository>();
        settings.Setup(s => s.GetByLegalEntityAsync(It.IsAny<Guid>(), legalEntityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolidayCalendarSettings { DefaultCountryCode = "LK" });
        var sut = new NagerHolidaysProvider(client.Object, settings.Object);

        var result = await sut.ListHolidaysAsync(Guid.NewGuid(), [legalEntityId], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));

        var holiday = Assert.Single(result);
        Assert.Equal("Independence Day", holiday.Name);
        Assert.Equal(legalEntityId, holiday.LegalEntityId);
    }
}
