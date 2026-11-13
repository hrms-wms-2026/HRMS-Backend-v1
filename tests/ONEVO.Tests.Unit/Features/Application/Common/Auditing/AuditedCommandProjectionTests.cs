using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ONEVO.Application.Common.Auditing;
using ONEVO.Application.Common.Behaviors;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.CreatePaymentGateway;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.RotatePaymentGatewayCredential;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PaymentGateway.Commands.UpdatePaymentGatewayMetadata;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.ConfigurePlatformOAuthApp;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.Commands.RotatePlatformOAuthAppSecret;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformOAuthApps.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.CreatePlatformServiceKey;
using ONEVO.Application.Features.DevPlatform.SystemConfig.PlatformServiceKeys.Commands.RotatePlatformServiceKey;
using ONEVO.Tests.Unit.Features.Application.Common.Behaviors;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>
/// Checks every hand-written audit projection (F-07): each one runs on an empty command and a
/// failed response without throwing, uses a well-formed action code, and none can carry a secret.
/// </summary>
public class AuditedCommandProjectionTests
{
    // Recognisable fake values: if any of them reaches an audit row, the projection leaks secrets.
    private const string FakeSecret = "sk_live_FAKE_AUDIT_SECRET_7f3c91";
    private const string FakePrivateKey = "-----BEGIN PRIVATE KEY-----FAKE_AUDIT_PK_51ab";
    private const string FakeWebhookSecret = "whsec_FAKE_AUDIT_WEBHOOK_2d8e";
    private const string FakePublicKey = "pk_live_FAKE_AUDIT_PUBLIC_9a01";

    private static readonly Regex ActionPattern = new("^[a-z_]+\\.[a-z_]+$", RegexOptions.Compiled);
    private static readonly Regex TargetTypePattern = new("^[a-z_]+$", RegexOptions.Compiled);

    public static TheoryData<Type> AuditedCommandTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(IAuditedCommand).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IAuditedCommand).IsAssignableFrom(t))
                     .OrderBy(t => t.FullName))
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void The_audited_command_inventory_is_not_empty()
        => AuditedCommandTypes().Count.Should().BeGreaterThan(60);

    [Theory]
    [MemberData(nameof(AuditedCommandTypes))]
    public void Projection_handles_an_empty_command_and_a_missing_response(Type commandType)
    {
        var command = RuntimeHelpers.GetUninitializedObject(commandType);
        var auditedInterface = commandType.GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuditedCommand<>));
        var responseType = auditedInterface.GetGenericArguments()[0];
        var describe = auditedInterface.GetMethod(nameof(IAuditedCommand<object>.DescribeAudit))!;
        var defaultResponse = responseType.IsValueType ? Activator.CreateInstance(responseType) : null;

        AuditEntry entry;
        try
        {
            entry = (AuditEntry)describe.Invoke(command, [defaultResponse])!;
        }
        catch (TargetInvocationException ex)
        {
            throw new Xunit.Sdk.XunitException(
                $"{commandType.Name}.DescribeAudit threw on an empty command: {ex.InnerException}");
        }

        ActionPattern.IsMatch(entry.Action).Should().BeTrue($"{commandType.Name} action '{entry.Action}'");
        TargetTypePattern.IsMatch(entry.TargetType).Should().BeTrue($"{commandType.Name} target type '{entry.TargetType}'");
        entry.TargetType.Length.Should().BeLessThanOrEqualTo(50, "it must also fit audit_logs.resource_type");
        var serialize = () => AuditJson.Serialize(entry.Details);
        serialize.Should().NotThrow();
    }

    public static TheoryData<string, object> SecretBearingCommands() => new()
    {
        {
            "rotate OAuth secret",
            new RotatePlatformOAuthAppSecretCommand("github", FakeSecret, FakePrivateKey, Guid.NewGuid())
        },
        {
            "configure OAuth app",
            new ConfigurePlatformOAuthAppCommand(
                Provider: "zoom", AppName: "Zoom", LogoUrl: null, ClientId: "client-id", ClientSecret: FakeSecret,
                PrivateKey: FakePrivateKey, IsActive: true, ActorPlatformUserId: Guid.NewGuid())
        },
        {
            "rotate payment gateway credential",
            new RotatePaymentGatewayCredentialCommand(Guid.NewGuid(), FakeSecret, FakeWebhookSecret, Guid.NewGuid())
        },
        {
            "create payment gateway",
            new CreatePaymentGatewayCommand(
                GatewayKey: "stripe-live", Provider: "stripe", Environment: "live", DisplayName: "Stripe",
                LogoUrl: null, PublicKey: FakePublicKey, MerchantId: "acct_1", WebhookUrl: null, IsActive: true,
                SecretKey: FakeSecret, WebhookSecret: FakeWebhookSecret, CountryCodes: ["LK"],
                CountryNameSnapshots: ["Sri Lanka"], ActorPlatformUserId: Guid.NewGuid())
        },
        {
            "update payment gateway metadata",
            new UpdatePaymentGatewayMetadataCommand(
                Guid.NewGuid(), "Stripe", null, FakePublicKey, null, null, true, null, null, Guid.NewGuid())
        },
        {
            "create service key",
            new CreatePlatformServiceKeyCommand(
                "aws-ses", "AWS SES", FakeSecret, Guid.NewGuid(),
                new Dictionary<string, string> { ["secret_access_key"] = FakePrivateKey, ["region"] = "eu-west-1" })
        },
        {
            "rotate service key",
            new RotatePlatformServiceKeyCommand(
                "aws-ses", FakeSecret, Guid.NewGuid(),
                new Dictionary<string, string> { ["secret_access_key"] = FakePrivateKey })
        },
    };

    [Theory]
    [MemberData(nameof(SecretBearingCommands))]
    public void Projection_never_contains_a_secret(string scenario, object command)
    {
        var auditedInterface = command.GetType().GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAuditedCommand<>));
        var describe = auditedInterface.GetMethod(nameof(IAuditedCommand<object>.DescribeAudit))!;
        var entry = (AuditEntry)describe.Invoke(command, [null])!;

        var everything = string.Join('|', entry.Action, entry.TargetType, entry.TargetId, AuditJson.Serialize(entry.Details));
        everything.Should().NotContain(FakeSecret, scenario)
            .And.NotContain(FakePrivateKey, scenario)
            .And.NotContain(FakeWebhookSecret, scenario)
            .And.NotContain(FakePublicKey, scenario);
    }

    [Fact]
    public async Task Rotation_audit_row_records_the_rotation_but_not_the_secret()
    {
        var platformUser = new Mock<ICurrentPlatformUserContext>();
        platformUser.Setup(p => p.UserId).Returns(Guid.NewGuid());
        var clock = new Mock<IDateTimeProvider>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var rows = new AuditingBehaviorTests.FakePlatformAuditLogs();
        var behavior = new AuditingBehavior<RotatePlatformOAuthAppSecretCommand, Result<PlatformOAuthAppDto>>(
            new AuditingBehaviorTests.FakeUnitOfWork(),
            rows,
            new AuditingBehaviorTests.FakeTenantAuditLogs(),
            platformUser.Object,
            new Mock<ICurrentUser>().Object,
            new Mock<IRequestClientInfo>().Object,
            clock.Object,
            [],
            NullLogger<AuditingBehavior<RotatePlatformOAuthAppSecretCommand, Result<PlatformOAuthAppDto>>>.Instance);

        await behavior.Handle(
            new RotatePlatformOAuthAppSecretCommand("github", FakeSecret, FakePrivateKey, Guid.NewGuid()),
            _ => Task.FromResult(Result<PlatformOAuthAppDto>.Success(new PlatformOAuthAppDto { Provider = "github" })),
            CancellationToken.None);

        var row = rows.Rows.Should().ContainSingle().Subject;
        row.Action.Should().Be("oauth_app.secret_rotated");
        row.TargetId.Should().Be("github");
        row.AfterJson.Should().Contain("\"clientSecretRotated\":true");
        var serializedRow = System.Text.Json.JsonSerializer.Serialize(row);
        serializedRow.Should().NotContain(FakeSecret).And.NotContain(FakePrivateKey);
    }
}
