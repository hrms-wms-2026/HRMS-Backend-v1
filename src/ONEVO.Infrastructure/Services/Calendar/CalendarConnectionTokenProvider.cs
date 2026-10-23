using System.Net;
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
            // Only a genuine grant failure (the provider rejecting the refresh token itself -
            // HTTP 400 invalid_grant/invalid_client, or 401) means the user must reconnect.
            // Anything else (network failure, timeout, a provider's transient 5xx) is not an
            // auth problem - flipping to ReauthRequired for those would force every user to
            // reconnect over a passing outage. CalendarSyncService already uses this same
            // FailureCount/MaxConsecutiveFailures escalation for pull/push failures; mirroring
            // it here means a transient refresh failure just gets retried by the next scheduled
            // CalendarSyncJob run, same as a transient sync failure already does.
            var isGrantFailure = ex is HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized };

            if (isGrantFailure)
            {
                logger.LogWarning(ex, "Token refresh for connection {ConnectionId} was rejected by the provider; marking reauth_required.", connection.Id);
                // The owner hears about it once, when the connection first needs a new sign-in (plan C1 P1-6).
                if (notifications is not null && connection.Status != ExternalCalendarConnectionStatuses.ReauthRequired)
                    await notifications.NotifyConnectionProblemAsync(connection.TenantId, connection.UserId,
                        CalendarProviderLabels.For(connection.Provider), "reauth_required", ct);
                connection.Status = ExternalCalendarConnectionStatuses.ReauthRequired;
            }
            else
            {
                logger.LogWarning(ex, "Token refresh failed transiently for connection {ConnectionId} (attempt {FailureCount}); will retry on the next sync.", connection.Id, connection.FailureCount + 1);
                connection.FailureCount++;
                if (connection.FailureCount >= ExternalCalendarConnectionStatuses.MaxConsecutiveFailures)
                    connection.Status = ExternalCalendarConnectionStatuses.Failed;
            }

            connection.LastError = ex.Message;
            connections.Update(connection);
            await unitOfWork.SaveChangesAsync(ct);
            return null;
        }
    }
}
