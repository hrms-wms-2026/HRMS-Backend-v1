using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ONEVO.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBiometricSideReferencePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "left_reference_photo_file_id",
                table: "biometric_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "right_reference_photo_file_id",
                table: "biometric_profiles",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "left_reference_photo_file_id",
                table: "biometric_profiles");

            migrationBuilder.DropColumn(
                name: "right_reference_photo_file_id",
                table: "biometric_profiles");
        }
    }
}
