using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ONEVO.Infrastructure.Migrations;

namespace ONEVO.Tests.Unit.Features.DevPlatform.PlatformAccess;

public sealed class PlatformUserCredentialMigrationTests
{
    [Fact]
    public void Up_creates_only_platform_user_credentials()
    {
        var migration = new AddPlatformUserCredentials();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddPlatformUserCredentials).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        up!.Invoke(migration, new object[] { builder });

        var tables = builder.Operations.OfType<CreateTableOperation>().ToArray();
        Assert.Single(tables);
        Assert.Equal("platform_user_credentials", tables[0].Name);
        Assert.DoesNotContain(builder.Operations, operation =>
            operation is AddColumnOperation or AlterColumnOperation or DropColumnOperation);
    }
}
