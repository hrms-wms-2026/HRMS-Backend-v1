using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStorageRlsPolicies : Migration
    {
        // file_records and file_upload_reservations were added after the
        // AddRlsPolicies migration and were never retrofitted into its table
        // list, leaving them with no RLS policy. Tenant isolation for both
        // tables must not rely solely on the EF Core HasQueryFilter, since
        // that filter's closure over ITenantContext is captured once by EF's
        // process-wide model cache and does not reflect per-request tenant
        // resolution. This migration closes that gap using the same
        // tenant_isolation policy pattern already proven for the tables in
        // AddRlsPolicies.
        private static readonly string[] TenantTables =
        [
            "file_records", "file_upload_reservations"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING      (tenant_id::text = current_setting('app.current_tenant_id', true))
                        WITH CHECK (tenant_id::text = current_setting('app.current_tenant_id', true));
                ");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }
        }
    }
}
