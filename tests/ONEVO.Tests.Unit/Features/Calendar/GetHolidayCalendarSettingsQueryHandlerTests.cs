using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Queries.GetHolidayCalendarSettings;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class GetHolidayCalendarSettingsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LegalEntityId = Guid.NewGuid();

    private static Mock<ICurrentUser> MakeCurrentUser(Guid? legalEntityId)
    {
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.TenantId).Returns(TenantId);
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUser.SetupGet(u => u.LegalEntityId).Returns(legalEntityId);
        return currentUser;
    }

    [Fact]
    public async Task Handle_ExistingSettings_ReturnsThem()
    {
        var existing = new HolidayCalendarSettings
        {
            Id = Guid.NewGuid(), TenantId = TenantId, LegalEntityId = LegalEntityId,
            DefaultCountryCode = "IN", HolidaySyncEnabled = true
        };
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetByLegalEntityAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var sut = new GetHolidayCalendarSettingsQueryHandler(MakeCurrentUser(LegalEntityId).Object, settingsRepo.Object);

        var result = await sut.Handle(new GetHolidayCalendarSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value!.Id);
        Assert.Equal("IN", result.Value.DefaultCountryCode);
    }

    [Fact]
    public async Task Handle_NoSettingsSeededYet_ReturnsNotFound()
    {
        var settingsRepo = new Mock<IHolidayCalendarSettingsRepository>();
        settingsRepo.Setup(s => s.GetByLegalEntityAsync(TenantId, LegalEntityId, It.IsAny<CancellationToken>())).ReturnsAsync((HolidayCalendarSettings?)null);

        var sut = new GetHolidayCalendarSettingsQueryHandler(MakeCurrentUser(LegalEntityId).Object, settingsRepo.Object);

        var result = await sut.Handle(new GetHolidayCalendarSettingsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task Handle_NoActiveLegalEntity_ReturnsUnprocessableEntity()
    {
        var sut = new GetHolidayCalendarSettingsQueryHandler(MakeCurrentUser(null).Object, Mock.Of<IHolidayCalendarSettingsRepository>());

        var result = await sut.Handle(new GetHolidayCalendarSettingsQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(422, result.StatusCode);
    }
}
