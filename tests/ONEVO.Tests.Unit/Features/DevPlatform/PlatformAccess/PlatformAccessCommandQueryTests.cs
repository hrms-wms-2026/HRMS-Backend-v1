using Moq;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformRolePermissions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.UpdatePlatformUserRoles;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSession;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Commands.RevokePlatformUserSessions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUsers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformUserDetail;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformRoles;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.GetPlatformRoleDetail;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformPermissions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformUserSessions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Queries.ListPlatformAuthEvents;

using ONEVO.Application.Features.DevPlatform.PlatformAccess.Helpers;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.RepositoryInterfaces;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.ServiceInterfaces;
using ONEVO.Application.Common.Exceptions;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public class PlatformAccessCommandQueryTests
{
    private readonly Mock<IPlatformRoleRepository> _roleRepoMock = new();
    private readonly Mock<IPlatformUserRepository> _userRepoMock = new();
    private readonly Mock<IPlatformUserSessionRepository> _sessionRepoMock = new();
    private readonly Mock<IPlatformAccessManagementService> _accessServiceMock = new();
    private readonly Mock<ICurrentPlatformUserContext> _currentUserMock = new();

    public PlatformAccessCommandQueryTests()
    {
        _currentUserMock.Setup(c => c.UserId).Returns(Guid.NewGuid());
        _currentUserMock.Setup(c => c.PlatformPermissions).Returns(new List<string> { PlatformPermissionCatalog.RolesManage });
        _currentUserMock.Setup(c => c.HasPlatformPermission(It.IsAny<string>())).Returns(true);
    }

    [Fact]
    public async Task UpdateRolePermissions_RejectsUnknownPermissionCodes()
    {
        var roleId = Guid.NewGuid();
        var handler = new UpdatePlatformRolePermissionsCommandHandler(_roleRepoMock.Object, _accessServiceMock.Object, _currentUserMock.Object);
        _roleRepoMock.Setup(r => r.GetRoleByIdAsync(roleId, default))
                     .ReturnsAsync(new PlatformRole { Id = roleId });

        var command = new UpdatePlatformRolePermissionsCommand(roleId, new[] { "invalid.permission" });

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRolePermissions_ReplacesOldAssignmentsWithNew()
    {
        var roleId = Guid.NewGuid();
        var handler = new UpdatePlatformRolePermissionsCommandHandler(_roleRepoMock.Object, _accessServiceMock.Object, _currentUserMock.Object);
        var role = new PlatformRole { Id = roleId };
        _roleRepoMock.Setup(r => r.GetRoleByIdAsync(roleId, default)).ReturnsAsync(role);

        var command = new UpdatePlatformRolePermissionsCommand(roleId, new[] { PlatformPermissionCatalog.RolesManage });

        await handler.Handle(command, CancellationToken.None);

        _roleRepoMock.Verify(r => r.ReplacePermissionsAsync(roleId, It.IsAny<IEnumerable<string>>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateUserRoles_RejectsUnknownRoleIds()
    {
        var userId = Guid.NewGuid();
        var invalidRoleId = Guid.NewGuid();
        var handler = new UpdatePlatformUserRolesCommandHandler(_userRepoMock.Object, _roleRepoMock.Object, _accessServiceMock.Object, _currentUserMock.Object);

        _userRepoMock.Setup(u => u.GetByIdAsync(userId, default))
                     .ReturnsAsync(new PlatformUser { Id = userId });

        _roleRepoMock.Setup(r => r.ListRolesAsync(default)).ReturnsAsync(new List<PlatformRole>());

        var command = new UpdatePlatformUserRolesCommand(userId, new[] { invalidRoleId });

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUserRoles_RequiresRolesManagePermission()
    {
        _currentUserMock.Setup(c => c.HasPlatformPermission(PlatformPermissionCatalog.RolesManage)).Returns(false);
        var handler = new UpdatePlatformUserRolesCommandHandler(_userRepoMock.Object, _roleRepoMock.Object, _accessServiceMock.Object, _currentUserMock.Object);

        var command = new UpdatePlatformUserRolesCommand(Guid.NewGuid(), new List<Guid>());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeSession_SetsRevokedAt()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var session = new PlatformUserSession { Id = sessionId, AccountId = userId, RevokedAt = null };

        _currentUserMock.Setup(c => c.UserId).Returns(userId);
        _sessionRepoMock.Setup(s => s.GetByIdAsync(sessionId, default)).ReturnsAsync(session);

        var handler = new RevokePlatformUserSessionCommandHandler(_sessionRepoMock.Object, _currentUserMock.Object);
        var command = new RevokePlatformUserSessionCommand(userId, sessionId);

        await handler.Handle(command, CancellationToken.None);

        _sessionRepoMock.Verify(s => s.RevokeByIdAsync(sessionId, default), Times.Once);
    }
}
