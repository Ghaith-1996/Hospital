using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9DurableEscalationRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alert_version",
                table: "escalation_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "event_sequence",
                table: "escalation_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "handled_negative_responses",
                table: "escalation_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at_utc",
                table: "escalation_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "escalation_runs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_check_at_utc",
                table: "escalation_runs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "remaining_delay",
                table: "escalation_runs",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stop_reason",
                table: "escalation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_escalation_runs_id_organization_id",
                table: "escalation_runs",
                columns: new[] { "id", "organization_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_confirmed_escalation_plans_organization_id_alert_id_alert_v~",
                table: "confirmed_escalation_plans",
                columns: new[] { "organization_id", "alert_id", "alert_version", "policy_id", "policy_version" });

            migrationBuilder.CreateTable(
                name: "escalation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    step = table.Column<int>(type: "integer", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_selection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalation_events_escalation_runs_run_id_organization_id",
                        columns: x => new { x.run_id, x.organization_id },
                        principalTable: "escalation_runs",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_next_check_at_utc_lease_expires_at_utc",
                table: "escalation_runs",
                columns: new[] { "next_check_at_utc", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version",
                table: "escalation_runs",
                columns: new[] { "organization_id", "alert_id", "alert_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version_poli~",
                table: "escalation_runs",
                columns: new[] { "organization_id", "alert_id", "alert_version", "policy_id", "policy_version" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_organization_id_run_id_sequence",
                table: "escalation_events",
                columns: new[] { "organization_id", "run_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_run_id_organization_id",
                table: "escalation_events",
                columns: new[] { "run_id", "organization_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_escalation_runs_confirmed_escalation_plans_organization_id_~",
                table: "escalation_runs",
                columns: new[] { "organization_id", "alert_id", "alert_version", "policy_id", "policy_version" },
                principalTable: "confirmed_escalation_plans",
                principalColumns: new[] { "organization_id", "alert_id", "alert_version", "policy_id", "policy_version" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE TRIGGER escalation_events_append_only BEFORE UPDATE OR DELETE ON escalation_events
                FOR EACH ROW EXECUTE FUNCTION reject_escalation_history_mutation();
                CREATE FUNCTION protect_escalation_policy_version() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' AND (to_jsonb(NEW) - 'is_active') = (to_jsonb(OLD) - 'is_active') THEN
                        RETURN NEW;
                    END IF;
                    RAISE EXCEPTION 'Escalation policy versions are immutable' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER escalation_policy_immutable BEFORE UPDATE OR DELETE ON escalation_policies
                FOR EACH ROW EXECUTE FUNCTION protect_escalation_policy_version();
                CREATE TRIGGER escalation_step_immutable BEFORE UPDATE OR DELETE ON escalation_steps
                FOR EACH ROW EXECUTE FUNCTION reject_escalation_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER escalation_policy_immutable ON escalation_policies;
                DROP TRIGGER escalation_step_immutable ON escalation_steps;
                DROP FUNCTION protect_escalation_policy_version();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_escalation_runs_confirmed_escalation_plans_organization_id_~",
                table: "escalation_runs");

            migrationBuilder.DropTable(
                name: "escalation_events");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_escalation_runs_id_organization_id",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_next_check_at_utc_lease_expires_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version_poli~",
                table: "escalation_runs");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_confirmed_escalation_plans_organization_id_alert_id_alert_v~",
                table: "confirmed_escalation_plans");

            migrationBuilder.DropColumn(
                name: "alert_version",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "event_sequence",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "handled_negative_responses",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "next_check_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "remaining_delay",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "stop_reason",
                table: "escalation_runs");
        }
    }
}
