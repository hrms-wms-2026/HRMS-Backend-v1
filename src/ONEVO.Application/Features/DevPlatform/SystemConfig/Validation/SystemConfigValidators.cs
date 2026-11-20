using FluentValidation;
using ONEVO.Application.Common.Validation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.CreateIntegration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.LinkIntegrationModule;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.SetIntegrationActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UnlinkIntegrationModule;
using ONEVO.Application.Features.DevPlatform.SystemConfig.IntegrationCatalog.Commands.UpdateIntegration;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.UpdatePaymentGatewayMetadata;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.VerifyGatewayCredentials;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.SetPlatformOAuthAppActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ValidatePlatformOAuthAppConfig;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.SetPlatformServiceKeyActivation;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.UpdatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.VerifyPlatformServiceKey;

namespace ONEVO.Application.Features.DevPlatform.SystemConfig.Validation;

/// <summary>
/// Task 19b. Platform configuration and its secrets. Column sizes:
/// <list type="bullet">
/// <item>payment_gateway_configs: gateway_key 80, provider 50, environment 20, display_name 100, logo_url 500,
/// public_key 255, merchant_id 100, webhook_url 500. payment_gateway_country_routes: country_code 2,
/// country_name_snapshot 120.</item>
/// <item>platform_oauth_apps: provider 50, app_name 100, logo_url 500, client_id 200.</item>
/// <item>platform_service_keys: service_key 50, display_name 80.</item>
/// <item>integration_catalog: integration_key 50, display_name 100, connection_scope 20, crownus_app_provider 50,
/// logo_url 500. module_key is nvarchar(100) in the module catalog. description is nvarchar(max); 2,000 is
/// chosen.</item>
/// </list>
/// Secrets are encrypted into nvarchar(max) columns, so their caps are chosen: an API secret or webhook secret
/// is under 200 characters in practice, and a PEM private key for a 4096-bit RSA key is about 3,300. The caps leave
/// several times that room. Messages never echo a value. The handlers keep the known providers, environments,
/// scopes, required secrets and URL checks.
/// </summary>
public static class SystemConfigLimits
{
    public const int GatewayKeyMaxLength = 80;
    public const int GatewayProviderMaxLength = 50;
    public const int EnvironmentMaxLength = 20;
    public const int GatewayDisplayNameMaxLength = 100;
    public const int UrlMaxLength = 500;
    public const int PublicKeyMaxLength = 255;
    public const int MerchantIdMaxLength = 100;
    public const int CountryCodeMaxLength = 2;
    public const int CountryNameMaxLength = 120;

    /// <summary>There are fewer than 250 ISO 3166 country codes.</summary>
    public const int MaxCountries = 250;

    public const int OAuthProviderMaxLength = 50;
    public const int AppNameMaxLength = 100;
    public const int ClientIdMaxLength = 200;

    public const int ServiceKeyMaxLength = 50;
    public const int ServiceDisplayNameMaxLength = 80;

    public const int IntegrationKeyMaxLength = 50;
    public const int IntegrationDisplayNameMaxLength = 100;
    public const int IntegrationDescriptionMaxLength = 2_000;
    public const int ConnectionScopeMaxLength = 20;
    public const int CrownusAppProviderMaxLength = 50;
    public const int ModuleKeyMaxLength = 100;

    public const int SecretMaxLength = 4_096;
    public const int PrivateKeyMaxLength = 16_384;

    /// <summary>A credential map holds a handful of named fields (key id, secret, region...).</summary>
    public const int MaxCredentialFields = 20;
}

// ---- Payment gateways ----

public sealed class CreatePaymentGatewayCommandValidator : AbstractValidator<CreatePaymentGatewayCommand>
{
    public CreatePaymentGatewayCommandValidator()
    {
        RuleFor(x => x.GatewayKey).ValidName(SystemConfigLimits.GatewayKeyMaxLength, required: false);
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.GatewayProviderMaxLength, required: false);
        RuleFor(x => x.Environment).ValidName(SystemConfigLimits.EnvironmentMaxLength, required: false);
        RuleFor(x => x.DisplayName).ValidName(SystemConfigLimits.GatewayDisplayNameMaxLength, required: false);
        RuleFor(x => x.LogoUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
        RuleFor(x => x.PublicKey).ValidName(SystemConfigLimits.PublicKeyMaxLength, required: false);
        RuleFor(x => x.MerchantId).ValidName(SystemConfigLimits.MerchantIdMaxLength, required: false);
        RuleFor(x => x.WebhookUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
        RuleFor(x => x.SecretKey).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.WebhookSecret).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.CountryCodes).ValidStringList(SystemConfigLimits.MaxCountries, SystemConfigLimits.CountryCodeMaxLength);
        RuleFor(x => x.CountryNameSnapshots).ValidStringList(SystemConfigLimits.MaxCountries, SystemConfigLimits.CountryNameMaxLength);
    }
}

public sealed class UpdatePaymentGatewayMetadataCommandValidator : AbstractValidator<UpdatePaymentGatewayMetadataCommand>
{
    public UpdatePaymentGatewayMetadataCommandValidator()
    {
        RuleFor(x => x.DisplayName).ValidName(SystemConfigLimits.GatewayDisplayNameMaxLength, required: false);
        RuleFor(x => x.LogoUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
        RuleFor(x => x.PublicKey).ValidName(SystemConfigLimits.PublicKeyMaxLength, required: false);
        RuleFor(x => x.MerchantId).ValidName(SystemConfigLimits.MerchantIdMaxLength, required: false);
        RuleFor(x => x.WebhookUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
        RuleFor(x => x.CountryCodes).ValidStringList(SystemConfigLimits.MaxCountries, SystemConfigLimits.CountryCodeMaxLength);
        RuleFor(x => x.CountryNameSnapshots).ValidStringList(SystemConfigLimits.MaxCountries, SystemConfigLimits.CountryNameMaxLength);
    }
}

public sealed class RotatePaymentGatewayCredentialCommandValidator : AbstractValidator<RotatePaymentGatewayCredentialCommand>
{
    public RotatePaymentGatewayCredentialCommandValidator()
    {
        RuleFor(x => x.NewSecretKey).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.NewWebhookSecret).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
    }
}

public sealed class VerifyGatewayCredentialsCommandValidator : AbstractValidator<VerifyGatewayCredentialsCommand>
{
    public VerifyGatewayCredentialsCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.GatewayProviderMaxLength, required: false);
        RuleFor(x => x.Credentials).ValidStringMap(
            SystemConfigLimits.MaxCredentialFields, InputLimits.KeyMaxLength, SystemConfigLimits.PrivateKeyMaxLength);
    }
}

// ---- Platform OAuth apps ----

public sealed class ConfigurePlatformOAuthAppCommandValidator : AbstractValidator<ConfigurePlatformOAuthAppCommand>
{
    public ConfigurePlatformOAuthAppCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.OAuthProviderMaxLength, required: false);
        RuleFor(x => x.AppName).ValidName(SystemConfigLimits.AppNameMaxLength, required: false);
        RuleFor(x => x.LogoUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
        RuleFor(x => x.ClientId).ValidName(SystemConfigLimits.ClientIdMaxLength, required: false);
        RuleFor(x => x.ClientSecret).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.PrivateKey).ValidName(SystemConfigLimits.PrivateKeyMaxLength, required: false);
    }
}

public sealed class RotatePlatformOAuthAppSecretCommandValidator : AbstractValidator<RotatePlatformOAuthAppSecretCommand>
{
    public RotatePlatformOAuthAppSecretCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.OAuthProviderMaxLength, required: false);
        RuleFor(x => x.ClientSecret).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.PrivateKey).ValidName(SystemConfigLimits.PrivateKeyMaxLength, required: false);
    }
}

public sealed class SetPlatformOAuthAppActivationCommandValidator : AbstractValidator<SetPlatformOAuthAppActivationCommand>
{
    public SetPlatformOAuthAppActivationCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.OAuthProviderMaxLength, required: false);
    }
}

public sealed class ValidatePlatformOAuthAppConfigCommandValidator : AbstractValidator<ValidatePlatformOAuthAppConfigCommand>
{
    public ValidatePlatformOAuthAppConfigCommandValidator()
    {
        RuleFor(x => x.Provider).ValidName(SystemConfigLimits.OAuthProviderMaxLength, required: false);
    }
}

// ---- Platform service keys ----

public sealed class CreatePlatformServiceKeyCommandValidator : AbstractValidator<CreatePlatformServiceKeyCommand>
{
    public CreatePlatformServiceKeyCommandValidator()
    {
        RuleFor(x => x.ServiceKey).ValidName(SystemConfigLimits.ServiceKeyMaxLength, required: false);
        RuleFor(x => x.DisplayName).ValidName(SystemConfigLimits.ServiceDisplayNameMaxLength, required: false);
        RuleFor(x => x.ApiKey).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.Fields).ValidStringMap(
            SystemConfigLimits.MaxCredentialFields, InputLimits.KeyMaxLength, SystemConfigLimits.PrivateKeyMaxLength);
    }
}

public sealed class RotatePlatformServiceKeyCommandValidator : AbstractValidator<RotatePlatformServiceKeyCommand>
{
    public RotatePlatformServiceKeyCommandValidator()
    {
        RuleFor(x => x.ServiceKey).ValidName(SystemConfigLimits.ServiceKeyMaxLength, required: false);
        RuleFor(x => x.ApiKey).ValidName(SystemConfigLimits.SecretMaxLength, required: false);
        RuleFor(x => x.Fields).ValidStringMap(
            SystemConfigLimits.MaxCredentialFields, InputLimits.KeyMaxLength, SystemConfigLimits.PrivateKeyMaxLength);
    }
}

public sealed class UpdatePlatformServiceKeyCommandValidator : AbstractValidator<UpdatePlatformServiceKeyCommand>
{
    public UpdatePlatformServiceKeyCommandValidator()
    {
        RuleFor(x => x.ServiceKey).ValidName(SystemConfigLimits.ServiceKeyMaxLength, required: false);
        RuleFor(x => x.DisplayName).ValidName(SystemConfigLimits.ServiceDisplayNameMaxLength, required: false);
    }
}

public sealed class SetPlatformServiceKeyActivationCommandValidator : AbstractValidator<SetPlatformServiceKeyActivationCommand>
{
    public SetPlatformServiceKeyActivationCommandValidator()
    {
        RuleFor(x => x.ServiceKey).ValidName(SystemConfigLimits.ServiceKeyMaxLength, required: false);
    }
}

public sealed class VerifyPlatformServiceKeyCommandValidator : AbstractValidator<VerifyPlatformServiceKeyCommand>
{
    public VerifyPlatformServiceKeyCommandValidator()
    {
        RuleFor(x => x.ServiceKey).ValidName(SystemConfigLimits.ServiceKeyMaxLength, required: false);
    }
}

// ---- Integration catalog ----

/// <summary>Create and update share the catalog entry fields.</summary>
public interface IIntegrationCatalogInput
{
    string IntegrationKey { get; }
    string DisplayName { get; }
    string? Description { get; }
    string ConnectionScope { get; }
    string CrownusAppProvider { get; }
    string? LogoUrl { get; }
}

public abstract class IntegrationCatalogInputValidator<T> : AbstractValidator<T> where T : IIntegrationCatalogInput
{
    protected IntegrationCatalogInputValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(SystemConfigLimits.IntegrationKeyMaxLength, required: false);
        RuleFor(x => x.DisplayName).ValidName(SystemConfigLimits.IntegrationDisplayNameMaxLength, required: false);
        RuleFor(x => x.Description).ValidName(SystemConfigLimits.IntegrationDescriptionMaxLength, required: false);
        RuleFor(x => x.ConnectionScope).ValidName(SystemConfigLimits.ConnectionScopeMaxLength, required: false);
        RuleFor(x => x.CrownusAppProvider).ValidName(SystemConfigLimits.CrownusAppProviderMaxLength, required: false);
        RuleFor(x => x.LogoUrl).ValidName(SystemConfigLimits.UrlMaxLength, required: false);
    }
}

public sealed class CreateIntegrationCommandValidator : IntegrationCatalogInputValidator<CreateIntegrationCommand> { }

public sealed class UpdateIntegrationCommandValidator : IntegrationCatalogInputValidator<UpdateIntegrationCommand> { }

public sealed class SetIntegrationActivationCommandValidator : AbstractValidator<SetIntegrationActivationCommand>
{
    public SetIntegrationActivationCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(SystemConfigLimits.IntegrationKeyMaxLength, required: false);
    }
}

public sealed class LinkIntegrationModuleCommandValidator : AbstractValidator<LinkIntegrationModuleCommand>
{
    public LinkIntegrationModuleCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(SystemConfigLimits.IntegrationKeyMaxLength, required: false);
        RuleFor(x => x.ModuleKey).ValidName(SystemConfigLimits.ModuleKeyMaxLength, required: false);
    }
}

public sealed class UnlinkIntegrationModuleCommandValidator : AbstractValidator<UnlinkIntegrationModuleCommand>
{
    public UnlinkIntegrationModuleCommandValidator()
    {
        RuleFor(x => x.IntegrationKey).ValidName(SystemConfigLimits.IntegrationKeyMaxLength, required: false);
        RuleFor(x => x.ModuleKey).ValidName(SystemConfigLimits.ModuleKeyMaxLength, required: false);
    }
}
