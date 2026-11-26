using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// Merge of development (2026-11-26): development's inbox_items and inbox_item_assignees (task 37a) were created before
    /// the task 39d tenant keys, and the 39d rule gives every tenant_id column a key to tenants. Read-only check first,
    /// as in 39d: a tenant_id that is not a tenant stops the migration with 50039 naming the table. Nothing is changed.
    /// </summary>
    public partial class AddInboxTenantForeignKeysAfterMerge : Migration
    {
        public const string CheckInboxTenantIdsSql = """
        SET NOCOUNT ON;
        EXEC sp_set_session_context N'app.tenant_context_mode', N'admin';
        DECLARE @n int, @bad nvarchar(max) = N'';
        SELECT @n = COUNT(*) FROM inbox_items x WHERE NOT EXISTS (SELECT 1 FROM tenants t WHERE t.id = x.tenant_id);
        IF @n > 0 SET @bad = CONCAT(@bad, N' inbox_items.tenant_id=', @n);
        SELECT @n = COUNT(*) FROM inbox_item_assignees x WHERE NOT EXISTS (SELECT 1 FROM tenants t WHERE t.id = x.tenant_id);
        IF @n > 0 SET @bad = CONCAT(@bad, N' inbox_item_assignees.tenant_id=', @n);
        EXEC sp_set_session_context N'app.tenant_context_mode', NULL;
        IF @bad <> N'' BEGIN DECLARE @msg nvarchar(2048) = LEFT(CONCAT(N'Inbox tenant keys: rows whose tenant_id is not a tenant:', @bad, N'. Fix or report them before applying the migration; nothing was changed.'), 2048); THROW 50039, @msg, 1; END
        """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CheckInboxTenantIdsSql);

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_item_assignees_tenants_tenant_id",
                table: "inbox_item_assignees",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_inbox_items_tenants_tenant_id",
                table: "inbox_items",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_inbox_item_assignees_tenants_tenant_id",
                table: "inbox_item_assignees");

            migrationBuilder.DropForeignKey(
                name: "fk_inbox_items_tenants_tenant_id",
                table: "inbox_items");
        }
    }
}
