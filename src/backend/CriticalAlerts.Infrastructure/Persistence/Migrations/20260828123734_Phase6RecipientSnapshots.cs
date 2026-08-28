using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase6RecipientSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_alert_recipient_selections_alert_id_practitioner_id_channel",
                table: "alert_recipient_selections");

            migrationBuilder.AddColumn<string>(
                name: "directory_revision",
                table: "alert_recipient_selections",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "directory_source_updated_at_utc",
                table: "alert_recipient_selections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "on_call_snapshot",
                table: "alert_recipient_selections",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_recipient_selections_practitioner_role_id_organizatio~",
                table: "alert_recipient_selections",
                columns: new[] { "practitioner_role_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "UX_alert_recipient_selection_version_practitioner_channel",
                table: "alert_recipient_selections",
                columns: new[] { "alert_id", "alert_version", "practitioner_id", "channel" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_alert_recipient_selections_practitioner_roles_practitioner_~",
                table: "alert_recipient_selections",
                columns: new[] { "practitioner_role_id", "organization_id" },
                principalTable: "practitioner_roles",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_alert_recipient_selections_practitioner_roles_practitioner_~",
                table: "alert_recipient_selections");

            migrationBuilder.DropIndex(
                name: "IX_alert_recipient_selections_practitioner_role_id_organizatio~",
                table: "alert_recipient_selections");

            migrationBuilder.DropIndex(
                name: "UX_alert_recipient_selection_version_practitioner_channel",
                table: "alert_recipient_selections");

            migrationBuilder.DropColumn(
                name: "directory_revision",
                table: "alert_recipient_selections");

            migrationBuilder.DropColumn(
                name: "directory_source_updated_at_utc",
                table: "alert_recipient_selections");

            migrationBuilder.DropColumn(
                name: "on_call_snapshot",
                table: "alert_recipient_selections");

            migrationBuilder.CreateIndex(
                name: "IX_alert_recipient_selections_alert_id_practitioner_id_channel",
                table: "alert_recipient_selections",
                columns: new[] { "alert_id", "practitioner_id", "channel" },
                unique: true);
        }
    }
}
