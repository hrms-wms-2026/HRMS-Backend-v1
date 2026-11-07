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

            foreach (var (table, predicate) in RlsTableRegistry.Tables)
            {
                migrationBuilder.Sql(RlsSql.CreatePolicy(table, predicate));
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
            => throw new NotSupportedException("Tenant isolation is never removed by a migration.");
    }
}
