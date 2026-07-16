using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionTrialAndGracePeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "access_ends_at",
                table: "tenant_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_end_date",
                table: "tenant_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_start_date",
                table: "tenant_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "unpaid_grace_period_days",
                table: "tenant_subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "trial_period_days",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "unpaid_grace_period_days",
                table: "subscription_plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
                columns: new[] { "trial_period_days", "unpaid_grace_period_days" },
                values: new object[] { 30, 7 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_ends_at",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_end_date",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_start_date",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "unpaid_grace_period_days",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "trial_period_days",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "unpaid_grace_period_days",
                table: "subscription_plans");
        }
    }
}
