using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplianceDataProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "simulation_patient_reference",
                table: "alerts",
                newName: "simulation_patient_reference_legacy");

            migrationBuilder.AddColumn<byte[]>(
                name: "simulation_patient_reference_ciphertext",
                table: "alerts",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simulation_patient_reference_key_version",
                table: "alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simulation_patient_reference_purpose",
                table: "alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "selection_source",
                table: "alert_recipient_selections",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.CreateTable(
                name: "alert_source_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    source_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_source_revisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_source_revisions_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_source_revisions_users_created_by_user_id_organizatio~",
                        columns: x => new { x.created_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_source_revisions_alert_id_organization_id",
                table: "alert_source_revisions",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_source_revisions_created_by_user_id_organization_id",
                table: "alert_source_revisions",
                columns: new[] { "created_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "UX_alert_source_revisions_alert_id_alert_version",
                table: "alert_source_revisions",
                columns: new[] { "alert_id", "alert_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DO $$ BEGIN RAISE EXCEPTION 'ComplianceDataProtection is a one-way protected-data migration; restore a database backup before rolling back'; END $$;");
        }
    }
}
