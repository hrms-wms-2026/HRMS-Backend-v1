using System;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence.Rls;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingDelegates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meeting_delegates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    principal_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    delegate_employee_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meeting_delegates", x => x.id);
                    table.ForeignKey(
                        name: "fk_meeting_delegates_employees_delegate_employee_id",
                        column: x => x.delegate_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_meeting_delegates_employees_principal_employee_id",
                        column: x => x.principal_employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_delegates_delegate",
                table: "meeting_delegates",
                columns: new[] { "tenant_id", "delegate_employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_meeting_delegates_delegate_employee_id",
                table: "meeting_delegates",
                column: "delegate_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_meeting_delegates_principal_employee_id",
                table: "meeting_delegates",
                column: "principal_employee_id");

            migrationBuilder.CreateIndex(
                name: "ux_meeting_delegates_pair_unrevoked",
                table: "meeting_delegates",
                columns: new[] { "tenant_id", "principal_employee_id", "delegate_employee_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            // Tenant isolation, like every table in RlsTableRegistry.
            migrationBuilder.Sql(RlsSql.CreatePolicy("meeting_delegates", RlsPredicate.Tenant));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RlsSql.DropPolicy("meeting_delegates"));
            migrationBuilder.DropTable(
                name: "meeting_delegates");
        }
    }
}
