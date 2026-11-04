using FluentAssertions;
using MediatR;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.DTOs;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>
/// Every command that carries a credential is audited, and no credential value can reach the audit
/// row: each secret slot gets a recognisable fake value that must be absent from the stored JSON.
/// </summary>
public class CredentialCommandAuditTests
{
    private const string FakeSecret = "FAKE-SECRET-7c1e2b9d-must-not-be-audited";
    private const string FakePrivateKey = "FAKE-PRIVATE-KEY-0a6f-must-not-be-audited";
    private const string FakeWebhookSecret = "FAKE-WEBHOOK-SECRET-93be-must-not-be-audited";
    private const string FakeFieldSecret = "FAKE-FIELD-SECRET-44d2-must-not-be-audited";

    private readonly AuditingHarness _harness = new();

    private void AssertOneRowWithoutSecrets(string expectedAction)
    {
        _harness.PlatformRows.Should().ContainSingle();
        _harness.PlatformRows[0].Action.Should().Be(expectedAction);
        _harness.PlatformRows[0].ActorPlatformUserId.Should().Be(_harness.ActorPlatformUserId);

        var json = _harness.AllRowsJson();
        json.Should().NotContain(FakeSecret);
        json.Should().NotContain(FakePrivateKey);
        json.Should().NotContain(FakeWebhookSecret);
        json.Should().NotContain(FakeFieldSecret);
    }

    [Fact]
    public async Task RotatePlatformOAuthAppSecret_RecordsTheRotation_NotTheSecret()
    {
        var command = new RotatePlatformOAuthAppSecretCommand("google", FakeSecret, FakePrivateKey, Guid.NewGuid());

        await _harness.RunAsync(command, Result<PlatformOAuthAppDto>.Success(
            new PlatformOAuthAppDto { Provider = "google", ActiveCredentialVersion = 3, HasActiveCredential = true }));

        AssertOneRowWithoutSecrets("platform_oauth_app.secret_rotated");
        _harness.PlatformRows[0].TargetId.Should().Be("google");
        _harness.PlatformRows[0].AfterJson.Should().Contain("\"credentialVersion\":3").And.Contain("\"privateKeyReplaced\":true");
    }

    [Fact]
    public async Task ConfigurePlatformOAuthApp_RecordsSettings_NotTheSecrets()
    {
        var command = new ConfigurePlatformOAuthAppCommand(
            "github", "ONEVO", null, "client-id-public", FakeSecret, FakePrivateKey, true, Guid.NewGuid());

        await _harness.RunAsync(command, Result<PlatformOAuthAppDto>.Success(
            new PlatformOAuthAppDto { Provider = "github", IsActive = true, ActiveCredentialVersion = 1 }));

        AssertOneRowWithoutSecrets("platform_oauth_app.configured");
        _harness.PlatformRows[0].AfterJson.Should().Contain("client-id-public").And.Contain("\"clientSecretReplaced\":true");
    }

    [Fact]
    public async Task RotatePaymentGatewayCredential_RecordsTheRotation_NotTheSecrets()
    {
        var gatewayId = Guid.NewGuid();
        var command = new RotatePaymentGatewayCredentialCommand(gatewayId, FakeSecret, FakeWebhookSecret, Guid.NewGuid());

        await _harness.RunAsync(command, Result<MediatR.Unit>.Success(MediatR.Unit.Value));

        AssertOneRowWithoutSecrets("payment_gateway.credential_rotated");
        _harness.PlatformRows[0].TargetId.Should().Be(gatewayId.ToString());
    }

    [Fact]
    public async Task CreatePaymentGateway_RecordsMetadata_NotTheSecrets()
    {
        var command = new CreatePaymentGatewayCommand(
            "stripe-live", "stripe", "live", "Stripe", null, "pk_live_public", "merchant-1", null, true,
            FakeSecret, FakeWebhookSecret, new[] { "LK" }, new string?[] { "Sri Lanka" }, Guid.NewGuid());
        var created = new PaymentGatewayConfigDto { Id = Guid.NewGuid(), ActiveCredentialVersion = 1 };

        await _harness.RunAsync(command, Result<PaymentGatewayConfigDto>.Success(created));

        AssertOneRowWithoutSecrets("payment_gateway.created");
        _harness.PlatformRows[0].TargetId.Should().Be(created.Id.ToString());
    }

    [Fact]
    public async Task CreatePlatformServiceKey_RecordsFieldNames_NotValues()
    {
        var command = new CreatePlatformServiceKeyCommand(
            "aws-ses", "AWS SES", FakeSecret, Guid.NewGuid(),
            new Dictionary<string, string> { ["secretAccessKey"] = FakeFieldSecret, ["region"] = FakeFieldSecret });

        await _harness.RunAsync(command, Result<PlatformServiceKeyDto>.Success(
            new PlatformServiceKeyDto { ServiceKey = "aws-ses", IsActive = true }));

        AssertOneRowWithoutSecrets("platform_service_key.created");
        _harness.PlatformRows[0].AfterJson.Should().Contain("secretAccessKey").And.Contain("region");
    }

    [Fact]
    public async Task RotatePlatformServiceKey_RecordsTheRotation_NotTheKeyOrFieldValues()
    {
        var command = new RotatePlatformServiceKeyCommand(
            "aws-ses", FakeSecret, Guid.NewGuid(),
            new Dictionary<string, string> { ["secretAccessKey"] = FakeFieldSecret });

        await _harness.RunAsync(command, Result<PlatformServiceKeyDto>.Success(
            new PlatformServiceKeyDto { ServiceKey = "aws-ses" }));

        AssertOneRowWithoutSecrets("platform_service_key.rotated");
        _harness.PlatformRows[0].TargetId.Should().Be("aws-ses");
    }

    [Fact]
    public async Task FailedRotation_WritesNoRow()
    {
        var command = new RotatePlatformOAuthAppSecretCommand("google", FakeSecret, null, Guid.NewGuid());

        await _harness.RunAsync(command, Result<PlatformOAuthAppDto>.NotFound("missing"));

        _harness.PlatformRows.Should().BeEmpty();
    }
}
