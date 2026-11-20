using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
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
using ONEVO.Application.Features.DevPlatform.SystemConfig.Validation;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.CompleteGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.DisconnectOwnUserIntegration;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.StartGitHubUserOAuth;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertOwnUserIntegrationConnection;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Commands.UpsertTenantIntegrationCredential;
using ONEVO.Application.Features.SharedPlatform.TenantIntegrations.Validation;
using static ONEVO.Tests.Unit.Features.Validation.Remaining.ValidationTestData;

namespace ONEVO.Tests.Unit.Features.Validation.Remaining;

/// <summary>Task 19b: platform configuration, credentials and the integration catalog.</summary>
public sealed class CredentialValidatorTests
{
    private static readonly Guid Actor = Guid.NewGuid();

    // ---- ValidStringMap ----

    private sealed record MapSample(IReadOnlyDictionary<string, string>? Map);

    private sealed class MapSampleValidator : AbstractValidator<MapSample>
    {
        public MapSampleValidator() => RuleFor(x => x.Map).ValidStringMap(2, 3, 4);
    }

    [Fact]
    public void ValidStringMap_BoundsCountKeysAndValues_WithoutEchoingValues()
    {
        var validator = new MapSampleValidator();
        validator.TestValidate(new MapSample(null)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new MapSample(new Dictionary<string, string> { ["abc"] = "abcd" })).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new MapSample(new Dictionary<string, string> { ["a"] = "", ["b"] = "", ["c"] = "" }))
            .ShouldHaveValidationErrorFor(x => x.Map);
        validator.TestValidate(new MapSample(new Dictionary<string, string> { ["abcd"] = "" })).ShouldHaveValidationErrorFor(x => x.Map);
        var secret = validator.TestValidate(new MapSample(new Dictionary<string, string> { ["k"] = "SECRET" }));
        secret.ShouldHaveValidationErrorFor(x => x.Map);
        secret.Errors.Should().OnlyContain(e => !e.ErrorMessage.Contains("SECRET"));
    }

    // ---- Payment gateways ----

    [Fact]
    public void CreateGateway_AtLimits_Passes_OverLimits_Fails()
    {
        var validator = new CreatePaymentGatewayCommandValidator();
        validator.TestValidate(new CreatePaymentGatewayCommand(S(80), S(50), S(20), S(100), S(500), S(255), S(100), S(500), true,
            S(4_096), S(4_096), Strings(250, 2), Strings(250, 120).Cast<string?>().ToList(), Actor)).ShouldNotHaveAnyValidationErrors();

        var result = validator.TestValidate(new CreatePaymentGatewayCommand(S(81), S(51), S(21), S(101), S(501), S(256), S(101), S(501), true,
            S(4_097), S(4_097), ["LKA"], [S(121)], Actor));
        foreach (var field in new[] { "GatewayKey", "Provider", "Environment", "DisplayName", "LogoUrl", "PublicKey", "MerchantId",
                     "WebhookUrl", "SecretKey", "WebhookSecret", "CountryCodes", "CountryNameSnapshots" })
            result.ShouldHaveValidationErrorFor(field);
    }

    [Fact]
    public void UpdateGatewayMetadata_NullsPass_TooManyCountriesFail()
    {
        var validator = new UpdatePaymentGatewayMetadataCommandValidator();
        validator.TestValidate(new UpdatePaymentGatewayMetadataCommand(Guid.NewGuid(), null, null, null, null, null, null, null, null, Actor))
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new UpdatePaymentGatewayMetadataCommand(Guid.NewGuid(), S(101), null, null, null, null, null, Strings(251, 2), null, Actor))
            .ShouldHaveValidationErrorFor(x => x.CountryCodes);
    }

    [Fact]
    public void RotateAndVerifyGateway_BoundSecrets()
    {
        new RotatePaymentGatewayCredentialCommandValidator().TestValidate(new RotatePaymentGatewayCredentialCommand(Guid.NewGuid(), S(4_097), null, Actor))
            .ShouldHaveValidationErrorFor(x => x.NewSecretKey);
        var verify = new VerifyGatewayCredentialsCommandValidator();
        verify.TestValidate(new VerifyGatewayCredentialsCommand("stripe", new Dictionary<string, string> { ["secret_key"] = "sk_test_x" }))
            .ShouldNotHaveAnyValidationErrors();
        verify.TestValidate(new VerifyGatewayCredentialsCommand("stripe", Enumerable.Range(0, 21).ToDictionary(i => $"k{i}", _ => "v")))
            .ShouldHaveValidationErrorFor(x => x.Credentials);
    }

    // ---- OAuth apps and service keys ----

    [Fact]
    public void OAuthApps_BoundProviderAndSecrets()
    {
        new ConfigurePlatformOAuthAppCommandValidator()
            .TestValidate(new ConfigurePlatformOAuthAppCommand("github", S(100), S(500), S(200), S(4_096), S(16_384), true, Actor))
            .ShouldNotHaveAnyValidationErrors();
        var result = new ConfigurePlatformOAuthAppCommandValidator()
            .TestValidate(new ConfigurePlatformOAuthAppCommand(S(51), S(101), S(501), S(201), S(4_097), S(16_385), true, Actor));
        foreach (var field in new[] { "Provider", "AppName", "LogoUrl", "ClientId", "ClientSecret", "PrivateKey" })
            result.ShouldHaveValidationErrorFor(field);

        new RotatePlatformOAuthAppSecretCommandValidator().TestValidate(new RotatePlatformOAuthAppSecretCommand("github", S(4_097), null, Actor))
            .ShouldHaveValidationErrorFor(x => x.ClientSecret);
        new SetPlatformOAuthAppActivationCommandValidator().TestValidate(new SetPlatformOAuthAppActivationCommand(S(51), true, Actor))
            .ShouldHaveValidationErrorFor(x => x.Provider);
        new ValidatePlatformOAuthAppConfigCommandValidator().TestValidate(new ValidatePlatformOAuthAppConfigCommand("zoom", Actor))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ServiceKeys_BoundKeyNameSecretAndFields()
    {
        new CreatePlatformServiceKeyCommandValidator()
            .TestValidate(new CreatePlatformServiceKeyCommand(S(50), S(80), S(4_096), Actor, new Dictionary<string, string> { ["region"] = "eu" }))
            .ShouldNotHaveAnyValidationErrors();
        var result = new CreatePlatformServiceKeyCommandValidator()
            .TestValidate(new CreatePlatformServiceKeyCommand(S(51), S(81), S(4_097), Actor, new Dictionary<string, string> { [S(101)] = "x" }));
        foreach (var field in new[] { "ServiceKey", "DisplayName", "ApiKey", "Fields" })
            result.ShouldHaveValidationErrorFor(field);

        new RotatePlatformServiceKeyCommandValidator().TestValidate(new RotatePlatformServiceKeyCommand("resend", S(4_097), Actor))
            .ShouldHaveValidationErrorFor(x => x.ApiKey);
        new UpdatePlatformServiceKeyCommandValidator().TestValidate(new UpdatePlatformServiceKeyCommand("resend", S(81), Actor))
            .ShouldHaveValidationErrorFor(x => x.DisplayName);
        new SetPlatformServiceKeyActivationCommandValidator().TestValidate(new SetPlatformServiceKeyActivationCommand(S(51), true, Actor))
            .ShouldHaveValidationErrorFor(x => x.ServiceKey);
        new VerifyPlatformServiceKeyCommandValidator().TestValidate(new VerifyPlatformServiceKeyCommand("resend", Actor))
            .ShouldNotHaveAnyValidationErrors();
    }

    // ---- Integration catalog ----

    [Fact]
    public void IntegrationCatalog_AtLimits_Passes_OverLimits_Fails()
    {
        new CreateIntegrationCommandValidator()
            .TestValidate(new CreateIntegrationCommand(S(50), S(100), S(2_000), S(20), S(50), S(500), true, Actor))
            .ShouldNotHaveAnyValidationErrors();
        var result = new UpdateIntegrationCommandValidator()
            .TestValidate(new UpdateIntegrationCommand(S(51), S(101), S(2_001), S(21), S(51), S(501), true));
        foreach (var field in new[] { "IntegrationKey", "DisplayName", "Description", "ConnectionScope", "CrownusAppProvider", "LogoUrl" })
            result.ShouldHaveValidationErrorFor(field);

        new LinkIntegrationModuleCommandValidator().TestValidate(new LinkIntegrationModuleCommand("github", S(100), Actor)).ShouldNotHaveAnyValidationErrors();
        new UnlinkIntegrationModuleCommandValidator().TestValidate(new UnlinkIntegrationModuleCommand("github", S(101)))
            .ShouldHaveValidationErrorFor(x => x.ModuleKey);
        new SetIntegrationActivationCommandValidator().TestValidate(new SetIntegrationActivationCommand(S(51), true))
            .ShouldHaveValidationErrorFor(x => x.IntegrationKey);
    }

    // ---- Tenant and user integrations ----

    [Fact]
    public void UserConnection_AtLimits_Passes_OverLimits_Fails()
    {
        var validator = new UpsertOwnUserIntegrationConnectionCommandValidator();
        validator.TestValidate(new UpsertOwnUserIntegrationConnectionCommand("github", S(200), S(200), S(320), S(16_384), null, null, ["repo", "read:user"]))
            .ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new UpsertOwnUserIntegrationConnectionCommand(S(51), S(201), S(201), S(321), S(16_385), S(16_385), null,
            Strings(101).ToArray()));
        foreach (var field in new[] { "IntegrationKey", "ProviderUserId", "ProviderUsername", "ProviderEmail", "AccessToken", "RefreshToken", "ScopesGranted" })
            result.ShouldHaveValidationErrorFor(field);
    }

    [Fact]
    public void TenantCredential_BoundsItsColumns()
    {
        var validator = new UpsertTenantIntegrationCredentialCommandValidator();
        validator.TestValidate(new UpsertTenantIntegrationCredentialCommand(Guid.NewGuid(), "github", "t", null, null, [], "1", "org", Actor, "active"))
            .ShouldNotHaveAnyValidationErrors();
        var result = validator.TestValidate(new UpsertTenantIntegrationCredentialCommand(Guid.NewGuid(), S(51), S(16_385), null, null, [S(201)],
            S(201), S(201), Actor, S(21)));
        foreach (var field in new[] { "IntegrationKey", "AccessToken", "ScopesGranted", "ExternalAccountId", "ExternalAccountName", "Status" })
            result.ShouldHaveValidationErrorFor(field);
    }

    [Fact]
    public void GitHubOAuthAndDisconnect_BoundTheirInputs()
    {
        new StartGitHubUserOAuthCommandValidator().TestValidate(new StartGitHubUserOAuthCommand("/settings", "https://x/cb")).ShouldNotHaveAnyValidationErrors();
        new StartGitHubUserOAuthCommandValidator().TestValidate(new StartGitHubUserOAuthCommand(S(2_049), "https://x/cb"))
            .ShouldHaveValidationErrorFor(x => x.ReturnUrl);
        var complete = new CompleteGitHubUserOAuthCommandValidator().TestValidate(new CompleteGitHubUserOAuthCommand(S(4_097), S(2_049), S(2_049)));
        complete.ShouldHaveValidationErrorFor(x => x.Code);
        complete.ShouldHaveValidationErrorFor(x => x.State);
        complete.ShouldHaveValidationErrorFor(x => x.RedirectUri);
        new DisconnectOwnUserIntegrationCommandValidator().TestValidate(new DisconnectOwnUserIntegrationCommand(S(51)))
            .ShouldHaveValidationErrorFor(x => x.IntegrationKey);
    }
}
