using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalEmailDirectory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
        CREATE TABLE IF NOT EXISTS global_email_directory (
            email       TEXT        NOT NULL,
            tenant_id   UUID        NOT NULL,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
            CONSTRAINT pk_global_email_directory PRIMARY KEY (email, tenant_id),
            CONSTRAINT fk_ged_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ix_global_email_directory_email ON global_email_directory(email);
    ");
            // Deliberately NO RLS on this table — it is the cross-tenant lookup index.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS global_email_directory;");
        }
    }
}
