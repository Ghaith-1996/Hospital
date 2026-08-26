using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4DirectorySourceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_record_id",
                table: "practitioner_roles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "SIM-LEGACY");

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                table: "practitioner_roles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "SIM-DIRECTORY");

            migrationBuilder.AddColumn<string>(
                name: "source_record_id",
                table: "contact_endpoints",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "SIM-LEGACY");

            migrationBuilder.AddColumn<string>(
                name: "source_system",
                table: "contact_endpoints",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "SIM-DIRECTORY");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_record_id",
                table: "practitioner_roles");

            migrationBuilder.DropColumn(
                name: "source_system",
                table: "practitioner_roles");

            migrationBuilder.DropColumn(
                name: "source_record_id",
                table: "contact_endpoints");

            migrationBuilder.DropColumn(
                name: "source_system",
                table: "contact_endpoints");
        }
    }
}
