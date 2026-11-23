using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Integrity;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Architecture.Schema;

/// <summary>
/// The status check constraints follow the C# enums and constants (task 39). If a value is added in code,
/// the model constraint changes with it, but the database keeps the old one until a migration runs:
/// MigrationsAllowEveryValueTheCodeCanWrite fails until that migration exists.
/// </summary>
public sealed class StatusCheckConstraintTests
{
    public static TheoryData<string> RuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var rule in StatusCheckConstraints.Rules)
            data.Add(rule.Name);
        return data;
    }

    [Fact]
    public void RuleNamesAreUnique()
        => Assert.Equal(StatusCheckConstraints.Rules.Count, StatusCheckConstraints.Rules.Select(r => r.Name).Distinct().Count());

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void RuleNamesATextColumnOfItsEntity(string name)
    {
        var rule = Rule(name);
        var entity = SchemaIntegrityInventory.Model().FindEntityType(rule.Entity);
        Assert.NotNull(entity);
        Assert.Equal(rule.Table, entity.GetTableName());
        var table = StoreObjectIdentifier.Table(rule.Table, entity.GetSchema());
        var property = entity.GetProperties().SingleOrDefault(p => p.GetColumnName(table) == rule.Column);
        Assert.True(property is not null, $"{rule.Table} has no column {rule.Column}.");
        Assert.Equal(typeof(string), property.GetProviderClrType() ?? property.ClrType);
        Assert.NotEmpty(rule.AllowedValues());
        Assert.All(rule.AllowedValues(), value =>
            Assert.True(property.GetMaxLength() is not int max || value.Length <= max,
                $"'{value}' is longer than {rule.Table}.{rule.Column} ({property.GetMaxLength()})."));
    }

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void ModelConstraintIsTheRule(string name)
    {
        var rule = Rule(name);
        var check = SchemaIntegrityInventory.Model().FindEntityType(rule.Entity)!
            .GetCheckConstraints().SingleOrDefault(c => c.Name == rule.Name);
        Assert.NotNull(check);
        Assert.Equal(rule.Sql(), check.Sql);
    }

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void MigrationsAllowEveryValueTheCodeCanWrite(string name)
    {
        var rule = Rule(name);
        var entity = SnapshotModel().GetEntityTypes().SingleOrDefault(e => e.GetTableName() == rule.Table);
        var check = entity?.GetCheckConstraints().SingleOrDefault(c => c.Name == rule.Name);
        Assert.True(check is not null,
            $"No migration creates {rule.Name}. Run dotnet ef migrations add.");

        var allowed = Regex.Matches(check.Sql, "'((?:[^']|'')*)'").Select(m => m.Groups[1].Value.Replace("''", "'")).ToHashSet();
        var missing = rule.AllowedValues().Where(value => !allowed.Contains(value)).ToList();
        var extra = allowed.Where(value => !rule.AllowedValues().Contains(value)).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"{rule.Name} in the migrations differs from {rule.Values.Name}. Missing: [{string.Join(", ", missing)}], " +
            $"extra: [{string.Join(", ", extra)}]. Add a migration (dotnet ef migrations add).");
    }

    [Fact]
    public void AllowedValuesComeFromTheEnumOrConstants()
    {
        Assert.Equal(["Pending", "Succeeded", "Failed", "Expired"],
            StatusCheckConstraints.AllowedValues(typeof(ONEVO.Domain.Features.Monitoring.Biometrics.Entities.BiometricEnrollmentStatus)));
        Assert.Equal(["pending", "processing", "published", "failed"],
            StatusCheckConstraints.AllowedValues(typeof(ONEVO.Domain.Features.SharedPlatform.Entities.OutboxMessageStatus)));
        var attendance = StatusCheckConstraints.AllowedValues(
            typeof(ONEVO.Domain.Features.TimeAttendance.Entities.AttendanceRecord), "Status");
        Assert.Contains("late", attendance);
        Assert.DoesNotContain("web", attendance);
    }

    private static StatusCheckConstraints.Rule Rule(string name) => StatusCheckConstraints.Rules.Single(r => r.Name == name);

    private static IModel SnapshotModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        var dateTimeProvider = new SystemDateTimeProvider();
        using var context = new ApplicationDbContext(
            options,
            new AuditableEntityInterceptor(new AnonymousCurrentUser(), dateTimeProvider),
            new SoftDeleteInterceptor(dateTimeProvider),
            new DomainEventDispatchInterceptor(new NoOpPublisher()),
            new TenantContextAccessor());
        return context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
    }
}
