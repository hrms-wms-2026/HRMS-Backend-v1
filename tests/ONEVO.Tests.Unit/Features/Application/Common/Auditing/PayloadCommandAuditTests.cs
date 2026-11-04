using System.Text;
using System.Text.Json;
using FluentAssertions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.DevPlatform.AppCatalog.Commands.SetGlobalApplicationLogo;
using ONEVO.Application.Features.DevPlatform.AppCatalog.DTOs;
using ONEVO.Application.Features.DevPlatform.Compliance.Commands.CreateLegalDocumentVersion;
using ONEVO.Application.Features.DevPlatform.Compliance.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.Commands.CreateConfigurationTemplate;
using ONEVO.Application.Features.DevPlatform.ConfigurationTemplates.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.InvitePlatformManager;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ChangeTenantStatus;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

/// <summary>Commands whose payload is bulky or personal record a summary, not the payload.</summary>
public class PayloadCommandAuditTests
{
    private const string Marker = "PAYLOAD-MARKER-3e8a-must-not-be-audited";
    private readonly AuditingHarness _harness = new();

    [Fact]
    public async Task SetGlobalApplicationLogo_RecordsTypeAndSize_NotTheBytes()
    {
        var appId = Guid.NewGuid();
        var bytes = Encoding.ASCII.GetBytes(Marker);

        await _harness.RunAsync(
            new SetGlobalApplicationLogoCommand(appId, bytes, "image/png", Guid.NewGuid()),
            Result<GlobalApplicationDto>.Success(null!));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.Action.Should().Be("global_application.logo_set");
        row.TargetId.Should().Be(appId.ToString());
        row.AfterJson.Should().Contain($"\"sizeBytes\":{bytes.Length}");
        _harness.AllRowsJson().Should().NotContain(Marker).And.NotContain(Convert.ToBase64String(bytes));
    }

    [Fact]
    public async Task CreateLegalDocumentVersion_RecordsMetadataAndHash_NotTheContent()
    {
        using var content = JsonDocument.Parse("{}");
        var response = new LegalDocumentVersionDetailDto(
            Guid.NewGuid(), "terms", "1.0", "Terms", "draft", true, "all", null, null,
            content.RootElement.Clone(), Marker, Marker, "sha256:abc", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        await _harness.RunAsync(
            new CreateLegalDocumentVersionCommand("terms", "1.0", "Terms", Marker, Marker, Marker, true, "all", Guid.NewGuid()),
            Result<LegalDocumentVersionDetailDto>.Success(response));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.TargetId.Should().Be(response.Id.ToString());
        row.AfterJson.Should().Contain("sha256:abc");
        _harness.AllRowsJson().Should().NotContain(Marker);
    }

    [Fact]
    public async Task CreateConfigurationTemplate_DoesNotCopyThePayload()
    {
        using var payload = JsonDocument.Parse($"{{\"x\":\"{Marker}\"}}");

        await _harness.RunAsync(
            new CreateConfigurationTemplateCommand("k", "leave", "Name", null, new[] { "leave" }, null, payload.RootElement, false, Guid.NewGuid()),
            Result<ConfigurationTemplateDto>.Success(new ConfigurationTemplateDto(
                Guid.NewGuid(), "k", "leave", "Name", null, 1, new[] { "leave" }, null,
                payload.RootElement.Clone(), false, true, Guid.NewGuid(), DateTimeOffset.UtcNow, null)));

        _harness.PlatformRows.Should().ContainSingle();
        _harness.AllRowsJson().Should().NotContain(Marker);
    }

    [Fact]
    public async Task InvitePlatformManager_UsesTheHandlerTarget_AndOmitsEmailAndName()
    {
        var newUserId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await _harness.RunAsync(
            new InvitePlatformManagerCommand("invitee.private@example.com", "Private Name", new[] { roleId }),
            Result.Success(),
            trail => trail.RecordTarget(newUserId.ToString()));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.Action.Should().Be("platform_user.invited");
        row.TargetId.Should().Be(newUserId.ToString());
        row.AfterJson.Should().Contain(roleId.ToString());
        _harness.AllRowsJson().Should().NotContain("invitee.private@example.com").And.NotContain("Private Name");
    }

    [Fact]
    public async Task ChangeTenantStatus_StoresBeforeStatusAndAfterAction()
    {
        var tenantId = Guid.NewGuid();

        await _harness.RunAsync(
            new ChangeTenantStatusCommand(tenantId, "suspend", "billing overdue"),
            Result.Success(),
            trail => trail.RecordBefore(new { status = "Active" }));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.TargetTenantId.Should().Be(tenantId);
        row.BeforeJson.Should().Contain("Active");
        row.AfterJson.Should().Contain("suspend");
    }
}
