using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBiometricEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "biometric_enrollment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aws_session_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    region = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    challenge_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    confidence = table.Column<float>(type: "real", nullable: true),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biometric_enrollment_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "biometric_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_biometric_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_biometric_enrollment_attempts_tenant_employee_created",
                table: "biometric_enrollment_attempts",
                columns: new[] { "tenant_id", "employee_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_biometric_profiles_tenant_employee",
                table: "biometric_profiles",
                columns: new[] { "tenant_id", "employee_id" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE biometric_enrollment_attempts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE biometric_enrollment_attempts FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON biometric_enrollment_attempts;
                CREATE POLICY tenant_isolation ON biometric_enrollment_attempts
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

                ALTER TABLE biometric_profiles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE biometric_profiles FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON biometric_profiles;
                CREATE POLICY tenant_isolation ON biometric_profiles
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation ON biometric_enrollment_attempts;
                ALTER TABLE biometric_enrollment_attempts DISABLE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON biometric_profiles;
                ALTER TABLE biometric_profiles DISABLE ROW LEVEL SECURITY;
            ");

            migrationBuilder.DropTable(
                name: "biometric_enrollment_attempts");

            migrationBuilder.DropTable(
                name: "biometric_profiles");
        }
    }
}
