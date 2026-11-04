using FluentAssertions;
using ONEVO.Application.Common.Models;
using ONEVO.Application.Features.Auth.Roles.Commands.ArchiveRole;
using ONEVO.Application.Features.Auth.Roles.Commands.AssignRolePermissions;
using ONEVO.Application.Features.Auth.Roles.Commands.CreateRole;
using ONEVO.Application.Features.Auth.Roles.Commands.UpdateRole;
using ONEVO.Application.Features.Auth.Roles.DTOs.Responses;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.AdminAssignTenantRolePermissions;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.CreateTenant;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.RevokeTenantSession;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Requests;
using ONEVO.Application.Features.DevPlatform.Tenancy.DTOs.Responses;
using Xunit;

namespace ONEVO.Tests.Unit.Features.Application.Common.Auditing;

public class TenancyAndRoleCommandAuditTests
{
    private readonly AuditingHarness _harness = new();

    private static RoleDetailDto RoleDetail(Guid id) =>
        new(id, "Manager", "desc", false, Array.Empty<RolePermissionDto>(), Array.Empty<RolePermissionDto>(), DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task AdminAssignTenantRolePermissions_WritesPlatformRowWithTargetTenantAndBeforeAfter()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var oldPermission = Guid.NewGuid();
        var newPermission = Guid.NewGuid();
        var command = new AdminAssignTenantRolePermissionsCommand(tenantId, roleId, new[] { newPermission, newPermission });

        await _harness.RunAsync(
            command,
            Result<RoleDetailDto>.Success(RoleDetail(roleId)),
            trail => trail.RecordBefore(new { permissionIds = new[] { oldPermission } }));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.Action.Should().Be("tenant_role.permissions_assigned");
        row.TargetId.Should().Be(roleId.ToString());
        row.TargetTenantId.Should().Be(tenantId);
        row.BeforeJson.Should().Contain(oldPermission.ToString());
        row.AfterJson.Should().Contain(newPermission.ToString());
        _harness.TenantRows.Should().BeEmpty();
    }

    [Fact]
    public async Task RevokeTenantSession_WritesPlatformRow()
    {
        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await _harness.RunAsync(new RevokeTenantSessionCommand(tenantId, sessionId), Result.Success());

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.Action.Should().Be("tenant_session.revoked");
        row.TargetId.Should().Be(sessionId.ToString());
        row.TargetTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task CreateTenant_WritesPlatformRow_WithoutOwnerEmailOrName()
    {
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var command = new CreateTenantCommand(
            "Acme", "acme", "tech", "11-50", "Acme Pvt Ltd", null, "LK", "Asia/Colombo", "LKR",
            new SubscriptionInfo(Guid.NewGuid(), "monthly", "subscription"),
            new TenantOwnerInviteRequest("owner.private@example.com", "Ownerfirst", "Ownerlast", null, false, null));
        var response = new CreateTenantDraftResponseDto(
            tenantId, "provisioning", "owner_invite",
            new OwnerInviteResultDto(ownerUserId, DateTimeOffset.UtcNow.AddDays(7), "queued"));

        await _harness.RunAsync(command, Result<CreateTenantDraftResponseDto>.Success(response));

        var row = _harness.PlatformRows.Should().ContainSingle().Subject;
        row.Action.Should().Be("tenant.created");
        row.TargetTenantId.Should().Be(tenantId);
        row.AfterJson.Should().Contain("\"slug\":\"acme\"").And.Contain(ownerUserId.ToString());
        _harness.AllRowsJson().Should().NotContain("owner.private@example.com")
            .And.NotContain("Ownerfirst").And.NotContain("Ownerlast");
    }

    [Fact]
    public async Task TenantRoleCommands_WriteToTheTenantAuditLog()
    {
        var roleId = Guid.NewGuid();

        await _harness.RunAsync(new CreateRoleCommand("Manager", null, new[] { Guid.NewGuid() }), Result<RoleDetailDto>.Success(RoleDetail(roleId)));
        await _harness.RunAsync(new UpdateRoleCommand(roleId, "Lead", null),
            Result<RoleSummaryDto>.Success(new RoleSummaryDto(roleId, "Lead", "", false, 0, DateTimeOffset.UtcNow, null)),
            trail => trail.RecordBefore(new { name = "Manager" }));
        await _harness.RunAsync(new AssignRolePermissionsCommand(roleId, new[] { Guid.NewGuid() }), Result<RoleDetailDto>.Success(RoleDetail(roleId)));
        await _harness.RunAsync(new ArchiveRoleCommand(roleId), Result.Success());

        _harness.PlatformRows.Should().BeEmpty();
        _harness.TenantRows.Select(r => r.Action).Should().Equal(
            "role.created", "role.updated", "role.permissions_assigned", "role.archived");
        _harness.TenantRows.Should().OnlyContain(r => r.TenantId == _harness.TenantId && r.UserId == _harness.TenantUserId);
        _harness.TenantRows.Should().OnlyContain(r => r.ResourceId == roleId);
        _harness.TenantRows[1].OldValuesJson.Should().Contain("Manager");
    }
}
