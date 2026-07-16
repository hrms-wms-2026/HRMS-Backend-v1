using FluentAssertions;
using ONEVO.Application.Features.InfrastructureModule.CountryDefaults.Queries.GetCountryDefaults;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Infrastructure;

public class GetCountryDefaultsQueryHandlerTests
{
    private readonly GetCountryDefaultsQueryHandler _handler = new();

    [Fact]
    public async Task Handle_LK_ReturnsColomboAndLKR()
    {
        var result = await _handler.Handle(
            new GetCountryDefaultsQuery("LK"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.CountryCode.Should().Be("LK");
        result.Value.DefaultTimezone.Should().Be("Asia/Colombo");
        result.Value.Timezones.Should().ContainSingle();
        result.Value.DefaultCurrency.Should().Be("LKR");
    }

    [Fact]
    public async Task Handle_MultiTimezoneCountry_ReturnsMultipleTimezones()
    {
        var result = await _handler.Handle(
            new GetCountryDefaultsQuery("US"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Timezones.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task Handle_UnknownCountry_Returns404()
    {
        var result = await _handler.Handle(
            new GetCountryDefaultsQuery("XX"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }
}
