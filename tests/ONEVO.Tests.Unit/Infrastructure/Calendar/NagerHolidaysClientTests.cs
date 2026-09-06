using System.Net;
using Moq;
using Moq.Protected;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Calendar;

public sealed class NagerHolidaysClientTests
{
    [Fact]
    public async Task GetPublicHolidaysAsync_ParsesOnlyNationalHolidays()
    {
        const string json = """
        [
          {"date":"2026-01-01","localName":"New Year","name":"New Year's Day","countryCode":"AT","nationalHoliday":true},
          {"date":"2026-05-01","localName":"Staatsfeiertag","name":"State Holiday","countryCode":"AT","nationalHoliday":false}
        ]
        """;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        var result = await sut.GetPublicHolidaysAsync("AT", 2026, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("New Year's Day", result[0].Name);
        Assert.True(result[0].NationalHoliday);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_UnknownCountry_ReturnsEmptyNotError()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        var result = await sut.GetPublicHolidaysAsync("ZZ", 2026, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_ServerError_Throws()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler.Object) { BaseAddress = new Uri("https://nagerholidays.com/api/v4/") };
        var sut = new NagerHolidaysClient(httpClient);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetPublicHolidaysAsync("AT", 2026, CancellationToken.None));
    }
}
