using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase11AssistanceResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_alert_source_revisions_id_organization_id_alert_id_alert_ve~",
                table: "alert_source_revisions",
                columns: new[] { "id", "organization_id", "alert_id", "alert_version" });

            migrationBuilder.CreateTable(
                name: "alert_assistance_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    source_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    configuration_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    result_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    result_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    result_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_assistance_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_assistance_results_alert_source_revisions_source_revi~",
                        columns: x => new { x.source_revision_id, x.organization_id, x.alert_id, x.alert_version },
                        principalTable: "alert_source_revisions",
                        principalColumns: new[] { "id", "organization_id", "alert_id", "alert_version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_assistance_results_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_assistance_results_users_requested_by_user_id_organiz~",
                        columns: x => new { x.requested_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_assistance_results_alert_id_organization_id",
                table: "alert_assistance_results",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_assistance_results_organization_id_alert_id_kind_crea~",
                table: "alert_assistance_results",
                columns: new[] { "organization_id", "alert_id", "kind", "created_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_assistance_results_requested_by_user_id_organization_~",
                table: "alert_assistance_results",
                columns: new[] { "requested_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_assistance_results_source_revision_id_organization_id~",
                table: "alert_assistance_results",
                columns: new[] { "source_revision_id", "organization_id", "alert_id", "alert_version" });
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_assistance_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'Assistance evidence is immutable' USING ERRCODE = '23514'; END;
                $$;
                CREATE TRIGGER assistance_immutable BEFORE UPDATE OR DELETE OR TRUNCATE ON alert_assistance_results
                FOR EACH STATEMENT EXECUTE FUNCTION reject_assistance_mutation();
                ALTER TABLE alert_assistance_results ADD CONSTRAINT assistance_kind CHECK (kind IN ('Transcription', 'Structuring'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_assistance_results");

            migrationBuilder.Sql("DROP FUNCTION reject_assistance_mutation();");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_alert_source_revisions_id_organization_id_alert_id_alert_ve~",
                table: "alert_source_revisions");
        }
    }
}
