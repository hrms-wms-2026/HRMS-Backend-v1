using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ONEVO.Infrastructure.Persistence;

#nullable disable

namespace ONEVO.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261021130000_AddConversationMessageHides")]
public partial class AddConversationMessageHides : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "conversation_message_hides",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_conversation_message_hides", x => x.id);
                table.ForeignKey(
                    name: "fk_conversation_message_hides_conversation_messages_message_id",
                    column: x => x.message_id,
                    principalTable: "conversation_messages",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_conversation_message_hides_tenant_id_employee_id",
            table: "conversation_message_hides",
            columns: new[] { "tenant_id", "employee_id" });

        migrationBuilder.CreateIndex(
            name: "ux_conversation_message_hides_message_id_employee_id",
            table: "conversation_message_hides",
            columns: new[] { "message_id", "employee_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "conversation_message_hides");
}
