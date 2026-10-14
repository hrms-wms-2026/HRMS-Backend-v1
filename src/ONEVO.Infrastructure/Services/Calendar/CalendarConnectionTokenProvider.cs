using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.Services;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;

namespace ONEVO.Infrastructure.Services.Calendar;

public sealed class CalendarConnectionTokenProvider(
    IExternalCalendarConnectionRepository connections,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IPlatformOAuthAppResolver appResolver,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    ILogger<CalendarConnectionTokenProvider> logger,
    ICalendarNotificationSender? notifications = null)
    : ICalendarConnectionTokenProvider
{
    public async Task<string?> GetFreshAccessTokenAsync(ExternalCalendarConnection connection, string oauthProvider, CancellationToken ct)
    {
        var needsRefresh = connection.ExpiresAt is null || connection.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!needsRefresh && connection.AccessTokenEncrypted is not null)
            return encryption.DecryptBytes(connection.AccessTokenEncrypted);

        try
        {
            var app = await appResolver.GetActiveAppForProviderAsync(oauthProvider, ct);
            var credential = await appResolver.GetActiveCredentialForProviderAsync(oauthProvider, ct);
            if (app is null || credential is null)
                throw new InvalidOperationException($"No active OAuth app configured for provider '{oauthProvider}'.");

            var refreshToken = encryption.DecryptBytes(connection.RefreshTokenEncrypted);
            var tokens = await tokenExchangeClient.RefreshTokenAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, refreshToken, ct);

            connection.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
            connection.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken ?? refreshToken);
            connection.ExpiresAt = tokens.ExpiresAt;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return tokens.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Token refresh failed for connection {ConnectionId}; marking reauth_required.", connection.Id);
            // The owner hears about it once, when the connection first needs a new sign-in (plan C1 P1-6).
            if (notifications is not null && connection.Status != ExternalCalendarConnectionStatuses.ReauthRequired)
                await notifications.NotifyConnectionProblemAsync(connection.TenantId, connection.UserId,
                    CalendarProviderLabels.For(connection.Provider), "reauth_required", ct);
            connection.Status = ExternalCalendarConnectionStatuses.ReauthRequired;
            connection.LastError = ex.Message;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }
}
