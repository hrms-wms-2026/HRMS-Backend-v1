using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ONEVO.Domain.Features.IdentityVerification.Entities;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity;
using ONEVO.Infrastructure.Migrations;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Architecture;

public sealed class UserProfileArchitectureTests
{
    [Fact]
    public void ProfileTenantTables_HaveEnabledAndForcedTenantIsolationPolicies()
    {
        var migration = new AddWorkLocationAndVerificationPhoto();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddWorkLocationAndVerificationPhoto).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        var sql = string.Join(
            Environment.NewLine,
            builder.Operations.OfType<SqlOperation>().Select(operation => operation.Sql));

        foreach (var table in new[]
                 {
                     "employee_work_location_settings",
                     "verification_reference_photos"
                 })
        {
            Assert.Contains($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
            Assert.Contains($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY", sql, StringComparison.Ordinal);
            Assert.Contains($"CREATE POLICY tenant_isolation ON {table}", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ActiveReferencePhotoIndex_IsUniqueAndPartial()
    {
        using var context = CreateModelInspectionContext();
        var entityType = context.Model.FindEntityType(typeof(VerificationReferencePhoto));

        Assert.NotNull(entityType);

        var activePhotoIndex = entityType.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[]
                {
                    nameof(VerificationReferencePhoto.TenantId),
                    nameof(VerificationReferencePhoto.EmployeeId)
                }));

        Assert.NotNull(activePhotoIndex);
        Assert.True(activePhotoIndex.IsUnique);
        Assert.Equal("is_active = true", activePhotoIndex.GetFilter());
    }

    [Fact]
    public void Migration_CreatesUniquePartialActiveReferencePhotoIndex()
    {
        var migration = new AddWorkLocationAndVerificationPhoto();
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        var up = typeof(AddWorkLocationAndVerificationPhoto).GetMethod(
            "Up",
            BindingFlags.Instance | BindingFlags.NonPublic);

        up!.Invoke(migration, [builder]);

        var index = builder.Operations
            .OfType<CreateIndexOperation>()
            .Single(operation => operation.Table == "verification_reference_photos");

        Assert.Equal(new[] { "tenant_id", "employee_id" }, index.Columns);
        Assert.True(index.IsUnique);
        Assert.Equal("is_active = true", index.Filter);
    }

    private static ApplicationDbContext CreateModelInspectionContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"user-profile-architecture-{Guid.NewGuid()}")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), new SystemDateTimeProvider()),
            new SoftDeleteInterceptor(new SystemDateTimeProvider()),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
    }
}
