using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    public partial class AddLegalDocumentVersionsAndLegalAcceptanceRecords : Migration
    {
        private static readonly string[] TenantTables = ["legal_acceptance_records"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_gdpr_consent_records_tenant_id_user_id_consent_type",
                table: "gdpr_consent_records");

            migrationBuilder.RenameTable(
                name: "gdpr_consent_records",
                newName: "legal_acceptance_records");

            migrationBuilder.Sql(
                "ALTER TABLE legal_acceptance_records " +
                "RENAME CONSTRAINT pk_gdpr_consent_records TO pk_legal_acceptance_records;");

            migrationBuilder.AddColumn<string>(
                name: "document_type",
                table: "legal_acceptance_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_version",
                table: "legal_acceptance_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "legal_acceptance_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "required",
                table: "legal_acceptance_records",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "legal_acceptance_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                table: "legal_acceptance_records",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "legal_acceptance_records",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE legal_acceptance_records
                SET document_type = consent_type,
                    document_version = 'legacy_v0',
                    decision = CASE WHEN consented THEN 'accepted' ELSE 'declined' END,
                    required = TRUE,
                    decided_at = consented_at,
                    source = 'legacy_migration';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "document_type",
                table: "legal_acceptance_records",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "document_version",
                table: "legal_acceptance_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "decision",
                table: "legal_acceptance_records",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "required",
                table: "legal_acceptance_records",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "decided_at",
                table: "legal_acceptance_records",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "source",
                table: "legal_acceptance_records",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "consent_type",
                table: "legal_acceptance_records");

            migrationBuilder.DropColumn(
                name: "consented",
                table: "legal_acceptance_records");

            migrationBuilder.DropColumn(
                name: "consented_at",
                table: "legal_acceptance_records");

            migrationBuilder.CreateTable(
                name: "legal_document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    block_scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    publish_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_legal_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_legal_document_versions_platform_users_published_by_id",
                        column: x => x.published_by_id,
                        principalTable: "platform_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_legal_acceptance_records_tenant_id_user_id_document_type",
                table: "legal_acceptance_records",
                columns: new[] { "tenant_id", "user_id", "document_type" });

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_legal_document_versions_document_type_published
                ON legal_document_versions (document_type)
                WHERE status = 'published';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_document_type_status_is_required_pu",
                table: "legal_document_versions",
                columns: new[] { "document_type", "status", "is_required", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_document_type_version",
                table: "legal_document_versions",
                columns: new[] { "document_type", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_legal_document_versions_published_by_id",
                table: "legal_document_versions",
                column: "published_by_id");

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    CREATE POLICY tenant_isolation ON {table}
                        USING (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        )
                        WITH CHECK (
                            current_setting('app.tenant_context_mode', true) = 'admin'
                            OR (
                                current_setting('app.tenant_context_mode', true) = 'tenant'
                                AND tenant_id::text = current_setting('app.current_tenant_id', true)
                            )
                        );
                    ");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "legal_document_versions");

            migrationBuilder.DropIndex(
                name: "ix_legal_acceptance_records_tenant_id_user_id_document_type",
                table: "legal_acceptance_records");

            migrationBuilder.AddColumn<string>(
                name: "consent_type",
                table: "legal_acceptance_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "consented",
                table: "legal_acceptance_records",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "consented_at",
                table: "legal_acceptance_records",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE legal_acceptance_records
                SET consent_type = document_type,
                    consented = decision = 'accepted',
                    consented_at = decided_at;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "consent_type",
                table: "legal_acceptance_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "consented",
                table: "legal_acceptance_records",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "consented_at",
                table: "legal_acceptance_records",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.DropColumn(name: "document_type", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "document_version", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "decision", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "required", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "decided_at", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "user_agent", table: "legal_acceptance_records");
            migrationBuilder.DropColumn(name: "source", table: "legal_acceptance_records");

            migrationBuilder.Sql(
                "ALTER TABLE legal_acceptance_records " +
                "RENAME CONSTRAINT pk_legal_acceptance_records TO pk_gdpr_consent_records;");

            migrationBuilder.RenameTable(
                name: "legal_acceptance_records",
                newName: "gdpr_consent_records");

            migrationBuilder.CreateIndex(
                name: "ix_gdpr_consent_records_tenant_id_user_id_consent_type",
                table: "gdpr_consent_records",
                columns: new[] { "tenant_id", "user_id", "consent_type" });

            migrationBuilder.Sql(
                """
                ALTER TABLE gdpr_consent_records ENABLE ROW LEVEL SECURITY;
                ALTER TABLE gdpr_consent_records FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON gdpr_consent_records;
                CREATE POLICY tenant_isolation ON gdpr_consent_records
                    USING (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    )
                    WITH CHECK (
                        current_setting('app.tenant_context_mode', true) = 'admin'
                        OR (
                            current_setting('app.tenant_context_mode', true) = 'tenant'
                            AND tenant_id::text = current_setting('app.current_tenant_id', true)
                        )
                    );
                """);
        }
    }
}
