using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Rls;
using Xunit;

namespace ONEVO.Tests.Architecture;

/// <summary>
/// Every table in RlsTableRegistry gets its tenant-isolation policy from exactly one migration. The baseline
/// (20261108000000_AddRowLevelSecurity) covers the tables that existed then; a table added later creates its own policy
/// in the migration that creates the table. If the baseline also saw it, a fresh database would try to put a policy on
/// a table that does not exist yet (meeting_delegates, 2026-11-10).
/// </summary>
public sealed class RlsMigrationPolicyArchitectureTests
{
    private static readonly IReadOnlyList<(string Migration, string Sql)> UpSql = typeof(ApplicationDbContext).Assembly
        .GetTypes()
        .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
        .SelectMany(t =>
        {
            var migration = (Migration)Activator.CreateInstance(t)!;
            return migration.UpOperations.OfType<SqlOperation>().Select(op => (t.Name, op.Sql));
        })
        .ToList();

    [Fact]
    public void EveryRlsTable_GetsItsPolicyFromExactlyOneMigration()
    {
        var wrong = RlsTableRegistry.Tables
            .Select(t => (Table: t.Key, Migrations: UpSql.Where(u => u.Sql == RlsSql.CreatePolicy(t.Key, t.Value))
                .Select(u => u.Migration).ToList()))
            .Where(x => x.Migrations.Count != 1)
            .Select(x => $"{x.Table}: [{string.Join(", ", x.Migrations)}]")
            .ToList();

        Assert.True(wrong.Count == 0, "Policy created by not exactly one migration: " + string.Join("; ", wrong));
    }

    [Fact]
    public void TheMeetingDelegatesPolicy_ComesFromItsOwnMigration()
    {
        var policy = RlsSql.CreatePolicy("meeting_delegates", RlsPredicate.Tenant);

        Assert.Equal(["AddMeetingDelegates"], UpSql.Where(u => u.Sql == policy).Select(u => u.Migration));
    }
}
