using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertTenantIntegrationCredential;

namespace ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Validation;

/// <summary>
/// Task 19b. user_integration_connections: integration_key 50, provider_user_id and provider_username 200,
/// provider_email 320. tenant_integration_credentials: integration_key 50, external_account_id and
/// external_account_name 200, status 20. Access and refresh tokens are encrypted into nvarchar(max) columns; a
/// Microsoft access token (a JWT) runs to a few thousand characters, so 16,384 is chosen. Scopes are stored as one
/// nvarchar(max) value; 100 scopes of 200 characters each is far above any provider. OAuth codes and our own
/// state token use the calendar OAuth bounds. Redirect and return URLs are capped at 2,048, the practical URL
/// limit. Messages never echo a value. The handlers keep the known keys, statuses and the return-URL check.
/// </summary>
public static class TenantIntegrationLimits
{
    public const int IntegrationKeyMaxLength = 50;
    public const int ProviderUserIdMaxLength = 200;
    public const int ProviderUsernameMaxLength = 200;
    public const int ProviderEmailMaxLength = 320;
    public const int ExternalAccountMaxLength = 200;
    public const int StatusMaxLength = 20;
    public const int TokenMaxLength = 16_384;
    public const int MaxScopes = 100;
    public const int ScopeMaxLength = 200;
    public const int OAuthCodeMaxLength = 4_096;
    public const int OAuthStateMaxLength = 2_048;
    public const int UrlMaxLength = 2_048;
}

public sealed class UpsertOwnUserIntegrationConnectionCommandValidator : AbstractValidator<UpsertOwnUserIntegrationConnectionCommand>
{
    public UpsertOwnUserIntegrationConnectionCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(TenantIntegrationLimits.IntegrationKeyMaxLength, required: false);
        RuleFor(x => x.ProviderUserId).ValidName(TenantIntegrationLimits.ProviderUserIdMaxLength, required: false);
        RuleFor(x => x.ProviderUsername).ValidName(TenantIntegrationLimits.ProviderUsernameMaxLength, required: false);
        RuleFor(x => x.ProviderEmail).ValidName(TenantIntegrationLimits.ProviderEmailMaxLength, required: false);
        RuleFor(x => x.AccessToken).ValidName(TenantIntegrationLimits.TokenMaxLength, required: false);
        RuleFor(x => x.RefreshToken).ValidName(TenantIntegrationLimits.TokenMaxLength, required: false);
        RuleFor(x => x.ScopesGranted).ValidStringList(TenantIntegrationLimits.MaxScopes, TenantIntegrationLimits.ScopeMaxLength);
    }
}

public sealed class UpsertTenantIntegrationCredentialCommandValidator : AbstractValidator<UpsertTenantIntegrationCredentialCommand>
{
    public UpsertTenantIntegrationCredentialCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(TenantIntegrationLimits.IntegrationKeyMaxLength, required: false);
        RuleFor(x => x.AccessToken).ValidName(TenantIntegrationLimits.TokenMaxLength, required: false);
        RuleFor(x => x.RefreshToken).ValidName(TenantIntegrationLimits.TokenMaxLength, required: false);
        RuleFor(x => x.ScopesGranted).ValidStringList(TenantIntegrationLimits.MaxScopes, TenantIntegrationLimits.ScopeMaxLength);
        RuleFor(x => x.ExternalAccountId).ValidName(TenantIntegrationLimits.ExternalAccountMaxLength, required: false);
        RuleFor(x => x.ExternalAccountName).ValidName(TenantIntegrationLimits.ExternalAccountMaxLength, required: false);
        RuleFor(x => x.Status).ValidName(TenantIntegrationLimits.StatusMaxLength, required: false);
    }
}

public sealed class DisconnectOwnUserIntegrationCommandValidator : AbstractValidator<DisconnectOwnUserIntegrationCommand>
{
    public DisconnectOwnUserIntegrationCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(TenantIntegrationLimits.IntegrationKeyMaxLength, required: false);
    }
}

public sealed class StartGitHubUserOAuthCommandValidator : AbstractValidator<StartGitHubUserOAuthCommand>
{
    public StartGitHubUserOAuthCommandValidator()
    {
        RuleFor(x => x.ReturnUrl).ValidName(TenantIntegrationLimits.UrlMaxLength, required: false);
        RuleFor(x => x.RedirectUri).ValidName(TenantIntegrationLimits.UrlMaxLength, required: false);
    }
}

public sealed class CompleteGitHubUserOAuthCommandValidator : AbstractValidator<CompleteGitHubUserOAuthCommand>
{
    public CompleteGitHubUserOAuthCommandValidator()
    {
        RuleFor(x => x.Code).ValidName(TenantIntegrationLimits.OAuthCodeMaxLength, required: false);
        RuleFor(x => x.State).ValidName(TenantIntegrationLimits.OAuthStateMaxLength, required: false);
        RuleFor(x => x.RedirectUri).ValidName(TenantIntegrationLimits.UrlMaxLength, required: false);
    }
}
