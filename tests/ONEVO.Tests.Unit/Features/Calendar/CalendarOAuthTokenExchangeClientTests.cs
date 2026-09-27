using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

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
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

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
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

        var result = await sut.GetAccountAsync("google", "at-1", CancellationToken.None);

        Assert.Equal("me@example.com", result.AccountEmail);
        Assert.Equal("me@example.com", result.PrimaryCalendarId);
    }

    [Fact]
    public async Task ExchangeCodeAsync_ZoomTokenUrl_SendsBasicAuthAndOmitsCredentialsFromBody()
    {
        string? capturedAuthHeader = null;
        string? capturedBody = null;
        var handler = new StubHandler(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { access_token = "at-zoom", refresh_token = "rt-zoom", expires_in = 3600 });
        });
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

        var result = await sut.ExchangeCodeAsync("https://zoom.us/oauth/token", "zoom-client-id", "zoom-client-secret", "code", "https://onexso.com:7229/callback", CancellationToken.None);

        Assert.Equal("at-zoom", result.AccessToken);
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("zoom-client-id:zoom-client-secret"));
        Assert.Equal(expectedAuth, capturedAuthHeader);
        Assert.DoesNotContain("client_id", capturedBody);
        Assert.DoesNotContain("client_secret", capturedBody);
    }

    [Fact]
    public async Task ExchangeCodeAsync_GoogleTokenUrl_StillSendsCredentialsInBodyWithNoBasicAuth()
    {
        string? capturedAuthHeader = "unset";
        string? capturedBody = null;
        var handler = new StubHandler(request =>
        {
            capturedAuthHeader = request.Headers.Authorization?.ToString();
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonResponse(new { access_token = "at-1", refresh_token = "rt-1", expires_in = 3600 });
        });
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

        await sut.ExchangeCodeAsync("https://oauth2.googleapis.com/token", "client", "secret", "code", "https://localhost:7229/callback", CancellationToken.None);

        Assert.Null(capturedAuthHeader);
        Assert.Contains("client_id=client", capturedBody);
        Assert.Contains("client_secret=secret", capturedBody);
    }

    [Fact]
    public async Task GetAccountAsync_Zoom_ReturnsEmailWithNoCalendarInfo()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("https://api.zoom.us/v2/users/me", request.RequestUri!.ToString());
            return JsonResponse(new { email = "organizer@acme.com" });
        });
        var sut = new CalendarOAuthTokenExchangeClient(new HttpClient(handler), NullLogger<CalendarOAuthTokenExchangeClient>.Instance);

        var result = await sut.GetAccountAsync("zoom", "access-token", CancellationToken.None);

        Assert.Equal("organizer@acme.com", result.AccountEmail);
        Assert.Null(result.PrimaryCalendarId);
        Assert.Null(result.PrimaryCalendarName);
    }
}
