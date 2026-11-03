using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <summary>
    /// P-1: the claim and lease columns for outbox_messages (see OutboxClaimStore).
    /// <para>
    /// Deploy with no mixed-version instances: an instance from before this release still polls pending
    /// rows without claiming them, so it can load a row a new instance is claiming and both send it. Stop
    /// (or drain) every old instance before starting new ones - at the latest before Meetings:BulkInvite
    /// is switched on.
    /// </para>
    /// </summary>
    public partial class AddOutboxClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attempt_started_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "locked_by",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "locked_until",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processing_locked_until",
                table: "outbox_messages",
                column: "locked_until",
                filter: "status = 'processing'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Code from before claiming reads only pending rows: hand claimed ones back, or they are never sent.
            migrationBuilder.Sql("UPDATE outbox_messages SET status = 'pending' WHERE status = 'processing';");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_processing_locked_until",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempt_started_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "locked_by",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "locked_until",
                table: "outbox_messages");
        }
    }
}
