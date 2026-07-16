using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;
using ONEVO.Application.Common.ServiceInterfaces;
using ONEVO.Domain.Features.DevPlatform.PlatformAccess.Entities;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;
using ONEVO.Infrastructure.Services.SharedPlatform;
using Xunit;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

/// <summary>
/// Proves the EF model contains exactly the canonical Phase 1 Developer Platform
/// tables from the inventory, and that the noncanonical platform_admin_sessions
/// table is no longer mapped.
/// </summary>
public class PlatformSchemaTests
{
    private static ApplicationDbContext BuildInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var currentUser = new Mock<ICurrentUser>();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        var publisher = new Mock<IPublisher>();

        var auditInterceptor = new AuditableEntityInterceptor(currentUser.Object, dateTimeProvider.Object);
        var softDeleteInterceptor = new SoftDeleteInterceptor(dateTimeProvider.Object);
        var domainEventInterceptor = new DomainEventDispatchInterceptor(publisher.Object);

        return new ApplicationDbContext(options, auditInterceptor, softDeleteInterceptor, domainEventInterceptor, new Mock<ITenantContext>().Object);
    }

    [Theory]
    [InlineData(typeof(PlatformUser), "platform_users")]
    [InlineData(typeof(PlatformUserInvite), "platform_user_invites")]
    [InlineData(typeof(PlatformRole), "platform_roles")]
    [InlineData(typeof(PlatformPermission), "platform_permissions")]
    [InlineData(typeof(PlatformRolePermission), "platform_role_permissions")]
    [InlineData(typeof(PlatformUserRole), "platform_user_roles")]
    [InlineData(typeof(PlatformUserSession), "platform_user_sessions")]
    [InlineData(typeof(PlatformAuthEvent), "platform_auth_events")]
    public void Model_ContainsCanonicalPlatformTable(Type entityType, string tableName)
    {
        using var db = BuildInMemoryDb();

        var entity = db.Model.FindEntityType(entityType);

        entity.Should().NotBeNull($"{entityType.Name} must be part of the EF model");
        entity!.GetTableName().Should().Be(tableName);
    }

    [Fact]
    public void Model_DoesNotMap_PlatformAdminSessions()
    {
        using var db = BuildInMemoryDb();

        var mappedTables = db.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n is not null)
            .ToList();

        mappedTables.Should().NotContain("platform_admin_sessions");
    }

    [Fact]
    public void PlatformUserSession_StoresOnlyHashedValues()
    {
        using var db = BuildInMemoryDb();

        var entity = db.Model.FindEntityType(typeof(PlatformUserSession))!;
        var propertyNames = entity.GetProperties().Select(p => p.Name).ToList();

        propertyNames.Should().Contain("TokenHash");
        propertyNames.Should().Contain("CsrfTokenHash");

        // No raw token/key/csrf columns may exist on the session table.
        propertyNames.Should().NotContain("Token");
        propertyNames.Should().NotContain("RawToken");
        propertyNames.Should().NotContain("SessionKey");
        propertyNames.Should().NotContain("CsrfToken");
    }

    [Fact]
    public void PlatformUserInvite_StoresOnlyHashedInviteToken()
    {
        using var db = BuildInMemoryDb();

        var entity = db.Model.FindEntityType(typeof(PlatformUserInvite))!;
        var propertyNames = entity.GetProperties().Select(p => p.Name).ToList();

        propertyNames.Should().Contain("InviteTokenHash");
        propertyNames.Should().NotContain("InviteToken");
    }

    [Fact]
    public void PlatformUser_HasNoPasswordColumn_PerCanonicalSchema()
    {
        using var db = BuildInMemoryDb();

        var entity = db.Model.FindEntityType(typeof(PlatformUser))!;
        var propertyNames = entity.GetProperties().Select(p => p.Name).ToList();

        // developer-platform/database/schema.md defines no password storage on platform_users.
        propertyNames.Should().NotContain("Password");
        propertyNames.Should().NotContain("PasswordHash");
    }
}
