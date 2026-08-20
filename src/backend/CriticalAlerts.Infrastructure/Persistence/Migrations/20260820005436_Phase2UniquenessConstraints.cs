using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2UniquenessConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alert_field_confirmations_alert_id_alert_version_field_id",
                table: "alert_field_confirmations");

            migrationBuilder.CreateIndex(
                name: "UX_user_roles_organization_id_user_id_role_id",
                table: "user_roles",
                columns: new[] { "organization_id", "user_id", "role_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_alert_field_confirmations_alert_id_alert_version_field_id",
                table: "alert_field_confirmations",
                columns: new[] { "alert_id", "alert_version", "field_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_user_roles_organization_id_user_id_role_id",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "UX_alert_field_confirmations_alert_id_alert_version_field_id",
                table: "alert_field_confirmations");

            migrationBuilder.CreateIndex(
                name: "IX_alert_field_confirmations_alert_id_alert_version_field_id",
                table: "alert_field_confirmations",
                columns: new[] { "alert_id", "alert_version", "field_id" });
        }
    }
}
