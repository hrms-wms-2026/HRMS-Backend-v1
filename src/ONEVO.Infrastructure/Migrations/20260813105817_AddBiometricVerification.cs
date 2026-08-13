using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBiometricVerification : Migration
    {
        private static readonly string[] TenantTables =
        [
            "biometric_verification_attempts",
            "employee_biometric_profiles"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "biometric_verification_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attendance_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aws_session_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    aws_region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    challenge_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    aws_session_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    liveness_confidence = table.Column<double>(type: "double precision", nullable: true),
                    match_confidence = table.Column<double>(type: "double precision", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biometric_verification_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employee_biometric_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    region = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reference_storage_key = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consent_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    consent_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    enrollment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_registration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_biometric_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_biometric_verification_attempts_tenant_id_aws_session_id",
                table: "biometric_verification_attempts",
                columns: new[] { "tenant_id", "aws_session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_biometric_verification_attempts_tenant_id_employee_id_purpo",
                table: "biometric_verification_attempts",
                columns: new[] { "tenant_id", "employee_id", "purpose", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employee_biometric_profiles_tenant_employee_active",
                table: "employee_biometric_profiles",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true,
                filter: "status = 'active'");

            // PostgreSQL RLS — tenant isolation on both biometric tables
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
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($@"
                    DROP POLICY IF EXISTS tenant_isolation ON {table};
                    ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
                ");
            }

            migrationBuilder.DropTable(
                name: "biometric_verification_attempts");

            migrationBuilder.DropTable(
                name: "employee_biometric_profiles");
        }
    }
}
