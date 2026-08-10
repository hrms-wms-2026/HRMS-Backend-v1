using FluentAssertions;
using ONEVO.Application.Features.DevPlatform.PlatformAccess.Mappers;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public sealed class PlatformAccessMapperTests
{
    [Fact]
    public void Map_ActiveUserWithRole_ReturnsFullNameAndRole()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "manager@onevo.io",
            FullName = "Arun Selvan",
            Status = PlatformUser.StatusActive,
            CreatedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
        };

        var result = PlatformAccessMapper.Map(user, "Platform Manager");

        result.Id.Should().Be(user.Id);
        result.Email.Should().Be("manager@onevo.io");
        result.FullName.Should().Be("Arun Selvan");
        result.Role.Should().Be("Platform Manager");
        result.Status.Should().Be(PlatformUser.StatusActive);
        result.CreatedAt.Should().Be(user.CreatedAt);
    }

    [Fact]
    public void Map_UserWithNoRoleAssigned_ReturnsEmptyRoleString()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "noroles@onevo.io",
            FullName = "No Roles",
            Status = PlatformUser.StatusActive,
        };

        var result = PlatformAccessMapper.Map(user, string.Empty);

        result.Role.Should().Be(string.Empty);
    }

    [Fact]
    public void Map_InactiveUser_ReturnsInactiveStatus()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "inactive@onevo.io",
            FullName = "Inactive User",
            Status = PlatformUser.StatusInactive,
        };

        var result = PlatformAccessMapper.Map(user, "Support Manager");

        result.Status.Should().Be(PlatformUser.StatusInactive);
    }

    [Fact]
    public void Map_PendingUser_ReturnsPendingStatus()
    {
        var user = new PlatformUser
        {
            Id = Guid.NewGuid(),
            Email = "pending@onevo.io",
            FullName = "Pending User",
            Status = PlatformUser.StatusPending,
        };

        var result = PlatformAccessMapper.Map(user, "Support Manager");

        result.Status.Should().Be(PlatformUser.StatusPending);
    }
}
