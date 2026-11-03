using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

/// <summary>
/// The DAPI history seeder wrote "passed" face checks for Tharmi Rajendran (DAPI-0003) dated after
/// today, so the newest attempt was always a pass and her clock-in retry counter stayed at
/// "Attempt 1 of 3". Removes her attempts dated in the future at the time this runs.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021140000_RemoveTharmiFutureFaceAttempts")]
public partial class RemoveTharmiFutureFaceAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // face_verification_attempts is under FORCE RLS; without admin mode this deletes nothing.
        migrationBuilder.Sql("SELECT set_config('app.tenant_context_mode', 'admin', true);");
        migrationBuilder.Sql(@"
            DELETE FROM face_verification_attempts
            WHERE tenant_id = '6b0874ab-71db-401f-859f-bdd50c1317fb'
              AND employee_id = 'b3b02f88-3560-786f-f450-bf595178efca'
              AND created_at > now();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Seeded demo rows; nothing to restore.
    }
}
