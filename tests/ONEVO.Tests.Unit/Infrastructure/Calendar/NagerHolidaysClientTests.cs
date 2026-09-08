using System.Net;
using Moq;
using Moq.Protected;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Infrastructure.Services.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Infrastructure.Calendar;

public sealed class NagerHolidaysClientTests
{
    private static (Mock<HttpMessageHandler> handler, List<HttpRequestMessage> requests) StubHandler(HttpResponseMessage response)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(response);
        return (handler, requests);
    }

    private static NagerHolidaysClient ClientFor(Mock<HttpMessageHandler> handler) =>
        new(new HttpClient(handler.Object) { BaseAddress = new Uri("https://date.nager.at/api/v3/") });

    [Fact]
    public async Task GetPublicHolidaysAsync_RequestsYearThenCountry_OnTheRealNagerRoute()
    {
        var (handler, requests) = StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var sut = ClientFor(handler);

        await sut.GetPublicHolidaysAsync("US", 2026, CancellationToken.None);

        Assert.Equal("https://date.nager.at/api/v3/PublicHolidays/2026/US", requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_KeepsOnlyNationwidePublicHolidays()
    {
        // Real Nager.Date shape: no "nationalHoliday" flag. Nationwide == global:true with "Public" in types.
        const string json = """
        [
          {"date":"2026-01-01","localName":"New Year","name":"New Year's Day","countryCode":"US","global":true,"types":["Public","Bank"]},
          {"date":"2026-02-12","localName":"Lincoln's Birthday","name":"Lincoln's Birthday","countryCode":"US","global":false,"types":["Public"]},
          {"date":"2026-11-27","localName":"Day After Thanksgiving","name":"Day After Thanksgiving","countryCode":"US","global":true,"types":["Optional"]}
        ]
        """;
        var (handler, _) = StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var sut = ClientFor(handler);

        var result = await sut.GetPublicHolidaysAsync("US", 2026, CancellationToken.None);

        var holiday = Assert.Single(result);
        Assert.Equal("New Year's Day", holiday.Name);
        Assert.Equal(new DateOnly(2026, 1, 1), holiday.Date);
        Assert.True(holiday.NationalHoliday);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_UnknownCountry_ReturnsEmptyNotError()
    {
        var (handler, _) = StubHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        var sut = ClientFor(handler);

        var result = await sut.GetPublicHolidaysAsync("ZZ", 2026, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPublicHolidaysAsync_ServerError_Throws()
    {
        var (handler, _) = StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = ClientFor(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.GetPublicHolidaysAsync("AT", 2026, CancellationToken.None));
    }
}
