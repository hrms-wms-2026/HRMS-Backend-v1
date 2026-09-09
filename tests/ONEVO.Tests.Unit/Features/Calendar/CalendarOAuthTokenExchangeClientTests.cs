using System.Net;
using System.Text;
using System.Text.Json;
using ONEVO.Infrastructure.ExternalServices.Calendar;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Calendar;

public sealed class CalendarOAuthTokenExchangeClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task ExchangeCodeAsync_ParsesTokensAndComputesExpiry()
    {
        var handler = new StubHandler(_ => JsonResponse(new { access_token = "at-1", refresh_token = "rt-1", expires_in = 3600 }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.ExchangeCodeAsync("https://oauth2.googleapis.com/token", "client", "secret", "code", "https://localhost:7229/callback", CancellationToken.None);

        Assert.Equal("at-1", result.AccessToken);
        Assert.Equal("rt-1", result.RefreshToken);
        Assert.NotNull(result.ExpiresAt);
        Assert.True(result.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(59));
    }

    [Fact]
    public async Task RefreshTokenAsync_ProviderOmitsRefreshToken_KeepsOriginal()
    {
        var handler = new StubHandler(_ => JsonResponse(new { access_token = "at-2", expires_in = 3600 }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.RefreshTokenAsync("https://oauth2.googleapis.com/token", "client", "secret", "original-refresh-token", CancellationToken.None);

        Assert.Equal("at-2", result.AccessToken);
        Assert.Equal("original-refresh-token", result.RefreshToken);
    }

    [Fact]
    public async Task GetAccountAsync_Google_ReturnsPrimaryCalendarAsAccountEmail()
    {
        var handler = new StubHandler(_ => JsonResponse(new
        {
            items = new[]
            {
                new { id = "coworker@example.com", primary = false, summary = "Coworker" },
                new { id = "me@example.com", primary = true, summary = "me@example.com" }
            }
        }));
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler));

        var result = await sut.GetAccountAsync("google", "at-1", CancellationToken.None);

        Assert.Equal("me@example.com", result.AccountEmail);
        Assert.Equal("me@example.com", result.PrimaryCalendarId);
    }
}
