using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Calendar.RepositoryInterfaces;
using ONEVO.Application.Features.Calendar.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Calendar.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;

namespace ONEVO.Application.Features.Calendar.Commands.CompleteCalendarConnection;

public sealed class CompleteCalendarConnectionCommandHandler(
    ICalendarOAuthStateProtector stateProtector,
    ITenantRepository tenants,
    ITenantContextSwitcher tenantSwitcher,
    IPlatformOAuthAppResolver appResolver,
    ICalendarOAuthTokenExchangeClient tokenExchangeClient,
    IExternalCalendarConnectionRepository connections,
    IEncryptionService encryption,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<CompleteCalendarConnectionCommandHandler> logger)
    : IRequestHandler<CompleteCalendarConnectionCommand, Result<string>>
{
    public async Task<Result<string>> Handle(CompleteCalendarConnectionCommand request, CancellationToken ct)
    {
        if (!stateProtector.TryUnprotect(request.State, out var state) || state is null)
            return Result<string>.Failure("Invalid or expired connection request.", 400);

        if (state.ExpiresAtUtc <= DateTimeOffset.UtcNow || !string.Equals(state.Provider, request.Provider, StringComparison.OrdinalIgnoreCase))
            return Result<string>.Failure("Invalid or expired connection request.", 400);

        var tenant = await tenants.GetByIdAsync(state.TenantId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active)
            return Result<string>.Failure("This connection request is no longer valid.", 400);

        await tenantSwitcher.SwitchToTenantAsync(new TenantRegistryEntry(tenant.Id, tenant.Slug, tenant.Status, null), ct);

        var rootDomain = (configuration["Tenancy:RootDomain"] ?? string.Empty).Trim().TrimStart('.');
        var appBaseUrl = configuration["Urls:AppBaseUrl"] ?? string.Empty;
        Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var baseUri);
        var scheme = baseUri?.Scheme ?? "https";
        var port = baseUri?.Port ?? 443;

        string BuildRedirect(string query)
            => new UriBuilder(scheme, $"{tenant.Slug}.{rootDomain}", port, "/calendar") { Query = query }.Uri.ToString();

        var errorRedirect = BuildRedirect("connectionError=1");

        // The provider redirects here with no `code` at all when the user denies consent
        // (or on other provider-side error reasons) — we already have a valid tenant at this
        // point, so send them back to the app instead of surfacing a raw 400.
        if (string.IsNullOrEmpty(request.Code))
            return Result<string>.Success(errorRedirect);

        try
        {
            return await CompleteConnectionAsync(request, request.Code, state, tenant, errorRedirect, BuildRedirect, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Calendar OAuth callback failed after tenant switch. TenantId={TenantId} UserId={UserId} Provider={Provider}",
                tenant.Id, state.UserId, request.Provider);
            return Result<string>.Success(errorRedirect);
        }
    }

    private async Task<Result<string>> CompleteConnectionAsync(
        CompleteCalendarConnectionCommand request, string code, CalendarOAuthState state, Tenant tenant,
        string errorRedirect, Func<string, string> buildRedirect, CancellationToken ct)
    {
        var app = await appResolver.GetActiveAppForProviderAsync(request.Provider, ct);
        var credential = await appResolver.GetActiveCredentialForProviderAsync(request.Provider, ct);
        if (app is null || credential is null)
        {
            logger.LogWarning(
                "Calendar OAuth callback found no active OAuth app/credential for provider. TenantId={TenantId} UserId={UserId} Provider={Provider}",
                tenant.Id, state.UserId, request.Provider);
            return Result<string>.Success(errorRedirect);
        }

        var callbackBaseUrl = (configuration["Urls:CalendarOAuthCallbackBaseUrl"] ?? string.Empty).TrimEnd('/');
        var redirectUri = $"{callbackBaseUrl}/api/v1/calendar/connections/{request.Provider}/callback";

        var tokens = await tokenExchangeClient.ExchangeCodeAsync(app.TokenUrl, credential.ClientId, credential.ClientSecret, code, redirectUri, ct);
        var account = await tokenExchangeClient.GetAccountAsync(request.Provider, tokens.AccessToken, ct);

        var externalSource = request.Provider.Equals("google", StringComparison.OrdinalIgnoreCase)
            ? CalendarExternalSources.GoogleCalendar
            : CalendarExternalSources.OutlookCalendar;

        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            var existing = await connections.GetByTenantUserProviderAsync(tenant.Id, state.UserId, externalSource, innerCt);
            var now = DateTimeOffset.UtcNow;

            if (existing is not null)
            {
                existing.ExternalAccountEmail = account.AccountEmail;
                existing.ExternalCalendarId = account.PrimaryCalendarId;
                existing.ExternalCalendarName = account.PrimaryCalendarName;
                existing.AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken);
                if (tokens.RefreshToken is not null)
                    existing.RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken);
                existing.Status = ExternalCalendarConnectionStatuses.Active;
                existing.FailureCount = 0;
                existing.LastError = null;
                existing.ExpiresAt = tokens.ExpiresAt;
                existing.UpdatedAt = now;
                connections.Update(existing);
            }
            else
            {
                if (tokens.RefreshToken is null)
                {
                    logger.LogWarning(
                        "Calendar OAuth callback received no refresh token for a new connection. TenantId={TenantId} UserId={UserId} Provider={Provider}",
                        tenant.Id, state.UserId, request.Provider);
                    return Result<string>.Success(errorRedirect);
                }

                await connections.AddAsync(new ExternalCalendarConnection
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenant.Id,
                    UserId = state.UserId,
                    Provider = externalSource,
                    ExternalAccountEmail = account.AccountEmail,
                    ExternalCalendarId = account.PrimaryCalendarId,
                    ExternalCalendarName = account.PrimaryCalendarName,
                    AccessTokenEncrypted = encryption.EncryptBytes(tokens.AccessToken),
                    RefreshTokenEncrypted = encryption.EncryptBytes(tokens.RefreshToken),
                    ScopesJson = JsonSerializer.Serialize(app.DefaultScopes),
                    SyncDirection = CalendarSyncDirections.TwoWay,
                    Status = ExternalCalendarConnectionStatuses.Active,
                    ExpiresAt = tokens.ExpiresAt,
                    CreatedAt = now
                }, innerCt);
            }

            await unitOfWork.SaveChangesAsync(innerCt);
            return Result<string>.Success(buildRedirect($"connected={request.Provider}"));
        }, ct);
    }
}
