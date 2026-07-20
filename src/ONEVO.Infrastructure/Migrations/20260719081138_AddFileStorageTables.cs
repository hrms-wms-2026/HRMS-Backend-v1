using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFileStorageTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "file_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(700)", maxLength: 700, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    safe_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    detected_content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    checksum_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scan_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    scan_result_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    scan_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    storage_deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_records_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_records_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "file_upload_reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reserved_bytes = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reserved_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_file_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_upload_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_file_records_completed_file_record",
                        column: x => x.completed_file_record_id,
                        principalTable: "file_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_file_upload_reservations_users_reserved_by_user_id",
                        column: x => x.reserved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_file_records_tenant_id_status",
                table: "file_records",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_file_records_uploaded_by_user_id",
                table: "file_records",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_completed_file_record_id",
                table: "file_upload_reservations",
                column: "completed_file_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_reserved_by_user_id",
                table: "file_upload_reservations",
                column: "reserved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_file_upload_reservations_tenant_id_status_expires_at",
                table: "file_upload_reservations",
                columns: new[] { "tenant_id", "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_upload_reservations");

            migrationBuilder.DropTable(
                name: "file_records");
        }
    }
}
