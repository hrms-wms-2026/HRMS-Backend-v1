using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using ONEVO.Infrastructure.ExternalServices.Messaging;
using ONEVO.Infrastructure.Identity.CurrentUser;
using ONEVO.Infrastructure.Identity.Tenancy;
using ONEVO.Infrastructure.Identity.Time;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Interceptors;

namespace ONEVO.Tests.Architecture;

/// <summary>Guards the EF model against PostgreSQL leftovers, with no live server.</summary>
public sealed class SqlServerModelArchitectureTests
{
    private const int MaxNonClusteredKeyBytes = 1700;

    private static readonly Regex PostgresType = new(
        @"^(jsonb|uuid|xid|bytea|text|boolean|integer|double precision|character varying.*|timestamp.*|numeric.*)$|\[\]$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PostgresSql = new(
        @"=\s*(false|true)\b|::|""|\bILIKE\b|\bIS (NOT )?DISTINCT\b|\bnow\(\)|\bANY\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoColumnUsesAPostgresType()
    {
        var offenders = Model().GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => (entity, property)))
            .Where(item => item.entity.GetTableName() is not null)
            .Select(item => (name: $"{item.entity.GetTableName()}.{item.property.GetColumnName()}",
                type: item.property.GetColumnType()))
            .Where(item => PostgresType.IsMatch(item.type))
            .Select(item => $"{item.name}: {item.type}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoFilterUsesPostgresSyntax()
    {
        var offenders = Model().GetEntityTypes()
            .SelectMany(entity => entity.GetIndexes())
            .Where(index => index.GetFilter() is { } filter && PostgresSql.IsMatch(filter))
            .Select(index => $"{index.GetDatabaseName()}: {index.GetFilter()}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoCheckConstraintUsesPostgresSyntax()
    {
        var offenders = Model().GetEntityTypes()
            .SelectMany(entity => entity.GetCheckConstraints())
            .Where(check => PostgresSql.IsMatch(check.Sql))
            .Select(check => $"{check.Name}: {check.Sql}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void IndexKeysFitTheLimit()
    {
        var offenders = new List<string>();
        foreach (var entity in Model().GetEntityTypes().Where(e => e.GetTableName() is not null))
        {
            var keys = entity.GetIndexes()
                .Select(index => (name: index.GetDatabaseName(), properties: index.Properties))
                .Concat(entity.GetKeys().Select(key => (name: key.GetName(), properties: key.Properties)));
            foreach (var (name, properties) in keys)
            {
                var sizes = properties.Select(p => KeyBytes(p.GetColumnType())).ToList();
                if (sizes.Any(size => size is null) || sizes.Sum() > MaxNonClusteredKeyBytes)
                {
                    offenders.Add($"{name}: {string.Join(", ", properties.Select(p => p.GetColumnType()))}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void DatabaseCollationIsCaseSensitive()
        => Assert.Equal("Latin1_General_100_CS_AS_SC", Model().GetCollation());

    private static int? KeyBytes(string columnType)
    {
        var nvarchar = Regex.Match(columnType, @"^nvarchar\((\d+)\)$", RegexOptions.IgnoreCase);
        if (nvarchar.Success)
        {
            return int.Parse(nvarchar.Groups[1].Value) * 2;
        }

        return columnType.ToLowerInvariant() switch
        {
            "nvarchar(max)" or "varbinary(max)" => null,
            "uniqueidentifier" => 16,
            "datetimeoffset" => 10,
            "date" => 3,
            "bit" => 1,
            "int" => 4,
            "bigint" => 8,
            var other when other.StartsWith("binary", StringComparison.Ordinal) => 32,
            _ => 17,
        };
    }

    private static IModel Model()
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
        return context.GetService<IDesignTimeModel>().Model;
    }
}
