using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileGitHubIntegrationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE integration_catalog
                SET connection_scope = 'both'
                WHERE integration_key = 'github'
                  AND connection_scope <> 'both';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $migration$
                BEGIN
                    RAISE EXCEPTION 'ReconcileGitHubIntegrationScope is one-way because the previous catalog scope cannot be inferred safely.';
                END
                $migration$;
                """);
        }
    }
}
