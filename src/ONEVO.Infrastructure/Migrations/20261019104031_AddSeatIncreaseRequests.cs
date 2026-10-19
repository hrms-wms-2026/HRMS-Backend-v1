using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeatIncreaseRequests : Migration
    {
        // New tenant-owned table: idempotent tenant_isolation policy via the `TenantTables` + foreach convention.
        private static readonly string[] TenantTables =
        [
            "seat_increase_requests"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seat_increase_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    onboarding_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    requested_seats = table.Column<int>(type: "integer", nullable: false),
                    seats_at_request = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seat_increase_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_onboarding_drafts_onboarding_draft_id",
                        column: x => x.onboarding_draft_id,
                        principalTable: "onboarding_drafts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_seat_increase_requests_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_decided_by_user_id",
                table: "seat_increase_requests",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_onboarding_draft_id",
                table: "seat_increase_requests",
                column: "onboarding_draft_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_requested_by_user_id",
                table: "seat_increase_requests",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_tenant_id_onboarding_draft_id",
                table: "seat_increase_requests",
                columns: new[] { "tenant_id", "onboarding_draft_id" },
                unique: true,
                filter: "status = 'pending' AND onboarding_draft_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_seat_increase_requests_tenant_id_status",
                table: "seat_increase_requests",
                columns: new[] { "tenant_id", "status" });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seat_increase_requests");
        }
    }
}
