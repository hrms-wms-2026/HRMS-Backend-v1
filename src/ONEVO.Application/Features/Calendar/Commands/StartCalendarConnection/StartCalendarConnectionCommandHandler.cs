using System.Web;
using MediatR;
using Microsoft.Extensions.Configuration;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;

namespace ONEVO.Application.Features.Calendar.Commands.StartCalendarConnection;

public sealed class StartCalendarConnectionCommandHandler(
    ICurrentUser currentUser,
    IPlatformOAuthAppResolver appResolver,
    ICalendarOAuthStateProtector stateProtector,
    IConfiguration configuration)
    : IRequestHandler<StartCalendarConnectionCommand, Result<StartCalendarConnectionResponse>>
{
    private static readonly string[] SupportedProviders = ["google", "microsoft"];

    public async Task<Result<StartCalendarConnectionResponse>> Handle(StartCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<StartCalendarConnectionResponse>.Forbidden();

        if (!SupportedProviders.Contains(request.Provider, StringComparer.OrdinalIgnoreCase))
            return Result<StartCalendarConnectionResponse>.Failure("Unsupported calendar provider.", 400);

        var app = await appResolver.GetActiveAppForProviderAsync(request.Provider, ct);
        if (app is null)
            return Result<StartCalendarConnectionResponse>.Failure("This calendar provider is not configured.", 400);

        var now = DateTimeOffset.UtcNow;
        var state = new CalendarOAuthState(
            Nonce: Guid.NewGuid().ToString("N"),
            TenantId: currentUser.TenantId,
            UserId: currentUser.UserId,
            Provider: request.Provider,
            IssuedAtUtc: now,
            ExpiresAtUtc: now.AddMinutes(10));
        var protectedState = stateProtector.Protect(state);

        var callbackBaseUrl = (configuration["Urls:CalendarOAuthCallbackBaseUrl"] ?? string.Empty).TrimEnd('/');
        var redirectUri = $"{callbackBaseUrl}/api/v1/calendar/connections/{request.Provider}/callback";

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["client_id"] = app.ClientId;
        query["redirect_uri"] = redirectUri;
        query["response_type"] = "code";
        query["scope"] = string.Join(' ', app.DefaultScopes);
        query["state"] = protectedState;
        if (request.Provider.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            query["access_type"] = "offline";
            query["prompt"] = "consent";
        }

        var authorizeUrl = $"{app.AuthorizationUrl}?{query}";
        return Result<StartCalendarConnectionResponse>.Success(new StartCalendarConnectionResponse(authorizeUrl));
    }
}
