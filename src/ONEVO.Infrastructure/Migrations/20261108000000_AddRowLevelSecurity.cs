using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>Tenant isolation for every table in RlsTableRegistry.</summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261108000000_AddRowLevelSecurity")]
    public class AddRowLevelSecurity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var statement in RlsSql.CreateFunctions())
            {
                migrationBuilder.Sql(statement);
            }

            // Baseline only: later tables do not exist yet here and get their policy in their own migration.
            foreach (var (table, predicate) in RlsTableRegistry.Baseline)
            {
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, predicate));
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Tenant isolation is never removed by a migration.");
    }
}
