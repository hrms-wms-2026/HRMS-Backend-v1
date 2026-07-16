using FluentAssertions;
using Moq;
using ONEVO.Application.Common.RepositoryInterfaces;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Application.Features.Auth.Invite.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Login.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Permission.RepositoryInterfaces;
using ONEVO.Application.Features.Auth.Roles.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.Commands.ApplyRoleTemplate;
using ONEVO.Application.Features.DevPlatform.Billing.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Provisioning.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Subscription.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.Tenancy.RepositoryInterfaces;
using ONEVO.Domain.Features.Auth.Entities;
using ONEVO.Domain.Features.InfrastructureModule.Entities;
using Xunit;
using ONEVO.Application.Features.Auth.Permission.DTOs.Responses;
using ONEVO.Application.Features.Auth.Permission.ServiceInterfaces;

namespace ONEVO.Tests.Unit.Features.Tenancy;

public sealed class ApplyRoleTemplateCommandHandlerTests
{
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IRoleTemplateRepository> _templates = new();
    private readonly Mock<IRoleRepository> _roles = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissions = new();
    private readonly Mock<IPermissionRepository> _permissions = new();
    private readonly Mock<IModuleEntitlementService> _entitlements = new();
    private readonly Mock<ITenantPermissionCatalogService> _catalog = new();
    private readonly Mock<IUserRoleRepository> _userRoles = new();
    private readonly Mock<IPermissionVersionService> _permVer = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TemplateId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private ApplyRoleTemplateCommandHandler BuildSut() =>
        new(
            _tenants.Object,
            _templates.Object,
            _roles.Object,
            _rolePermissions.Object,
            _permissions.Object,
            _entitlements.Object,
            _catalog.Object,
            _userRoles.Object,
            _permVer.Object,
            _uow.Object);

    [Fact]
    public async Task Handle_TenantMissing_ReturnsNotFound()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyRoleTemplateCommand(TenantId, TemplateId, null, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
        _templates.Verify(t => t.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_AlreadyAppliedWithoutForce_ReturnsConflict()
    {
        _tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId });

        var template = new RoleTemplate
        {
            Id = TemplateId,
            Name = "HR Manager",
            Description = "d",
            ModuleKeysJson = """["core_hr"]""",
            PermissionCodesJson = """["employees:read"]""",
            IsActive = true
        };
        _templates.Setup(t => t.GetByIdAsync(TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var perm = new Permission
        {
            Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Code = "employees:read",
            Description = "x",
            Module = "employees"
        };
        _permissions.Setup(p => p.GetByCodesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { perm });

        _entitlements.Setup(e => e.GetAssignablePermissionsForTenantAsync(
                TenantId,
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Permission> { perm });

        _catalog.Setup(c => c.GetCatalogAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ONEVO.Application.Features.Auth.Permission.DTOs.Responses.PermissionCatalogDto(
                    [],
                    []));

        var linkedRole = new Role
        {
            Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            TenantId = TenantId,
            Name = "HR Manager",
            SourceTemplateId = TemplateId
        };
        _roles.Setup(r => r.GetBySourceTemplateForTenantAsync(TenantId, TemplateId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedRole);

        var sut = BuildSut();
        var result = await sut.Handle(
            new ApplyRoleTemplateCommand(TenantId, TemplateId, null, false),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("forceUpdate");
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
