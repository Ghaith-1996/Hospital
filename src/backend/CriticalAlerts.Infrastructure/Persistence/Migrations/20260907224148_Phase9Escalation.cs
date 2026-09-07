using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9Escalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "alert_version",
                table: "escalation_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_category",
                table: "escalation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at_utc",
                table: "escalation_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "escalation_runs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                table: "escalation_runs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at_utc",
                table: "escalation_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                table: "escalation_runs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_revision",
                table: "escalation_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "remaining_delay",
                table: "escalation_runs",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at_utc",
                table: "escalation_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_escalation_runs_id_organization_id",
                table: "escalation_runs",
                columns: new[] { "id", "organization_id" });

            migrationBuilder.CreateTable(
                name: "escalation_consumed_signals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_sequence = table.Column<int>(type: "integer", nullable: false),
                    consumed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_consumed_signals", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalation_consumed_signals_alert_recipient_selections_reci~",
                        columns: x => new { x.recipient_selection_id, x.organization_id },
                        principalTable: "alert_recipient_selections",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalation_consumed_signals_escalation_runs_run_id_organiza~",
                        columns: x => new { x.run_id, x.organization_id },
                        principalTable: "escalation_runs",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalation_consumed_signals_recipient_responses_response_id~",
                        columns: x => new { x.response_id, x.organization_id },
                        principalTable: "recipient_responses",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalation_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    step_sequence = table.Column<int>(type: "integer", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recipient_selection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalation_events_alert_recipient_selections_recipient_sele~",
                        columns: x => new { x.recipient_selection_id, x.organization_id },
                        principalTable: "alert_recipient_selections",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalation_events_escalation_runs_run_id_organization_id",
                        columns: x => new { x.run_id, x.organization_id },
                        principalTable: "escalation_runs",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_escalation_events_users_actor_user_id_organization_id",
                        columns: x => new { x.actor_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version",
                table: "escalation_runs",
                columns: new[] { "organization_id", "alert_id", "alert_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_plan_id_organization_id",
                table: "escalation_runs",
                columns: new[] { "plan_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_state_lease_expires_at_utc",
                table: "escalation_runs",
                columns: new[] { "state", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_consumed_signals_organization_id_run_id_response~",
                table: "escalation_consumed_signals",
                columns: new[] { "organization_id", "run_id", "response_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_consumed_signals_organization_id_run_id_step_seq~",
                table: "escalation_consumed_signals",
                columns: new[] { "organization_id", "run_id", "step_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_consumed_signals_recipient_selection_id_organiza~",
                table: "escalation_consumed_signals",
                columns: new[] { "recipient_selection_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_consumed_signals_response_id_organization_id",
                table: "escalation_consumed_signals",
                columns: new[] { "response_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_consumed_signals_run_id_organization_id",
                table: "escalation_consumed_signals",
                columns: new[] { "run_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_actor_user_id_organization_id",
                table: "escalation_events",
                columns: new[] { "actor_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_organization_id_run_id_occurred_at_utc",
                table: "escalation_events",
                columns: new[] { "organization_id", "run_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_organization_id_run_id_step_sequence_even~",
                table: "escalation_events",
                columns: new[] { "organization_id", "run_id", "step_sequence", "event_type" },
                unique: true,
                filter: "event_type NOT IN ('Paused', 'Resumed', 'RecipientActivated')");

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_organization_id_run_id_step_sequence_reci~",
                table: "escalation_events",
                columns: new[] { "organization_id", "run_id", "step_sequence", "recipient_selection_id" },
                unique: true,
                filter: "event_type = 'RecipientActivated'");

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_recipient_selection_id_organization_id",
                table: "escalation_events",
                columns: new[] { "recipient_selection_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_events_run_id_organization_id",
                table: "escalation_events",
                columns: new[] { "run_id", "organization_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_escalation_runs_alert_escalation_plans_plan_id_organization~",
                table: "escalation_runs",
                columns: new[] { "plan_id", "organization_id" },
                principalTable: "alert_escalation_plans",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
            // SQL composite keys preserve nullable legacy run bindings; an EF alternate key would require the version.
            migrationBuilder.Sql("""
                ALTER TABLE escalation_runs ADD CONSTRAINT uq_escalation_run_scope UNIQUE (id, organization_id, alert_id, alert_version);
                ALTER TABLE alert_escalation_plans ADD CONSTRAINT uq_escalation_plan_exact UNIQUE (id, organization_id, alert_id, alert_version, revision, escalation_policy_id, escalation_policy_version);
                ALTER TABLE escalation_runs ADD CONSTRAINT fk_escalation_run_exact_plan FOREIGN KEY (plan_id, organization_id, alert_id, alert_version, plan_revision, policy_id, policy_version)
                    REFERENCES alert_escalation_plans (id, organization_id, alert_id, alert_version, revision, escalation_policy_id, escalation_policy_version);
                ALTER TABLE escalation_runs ADD CONSTRAINT ck_escalation_run_binding CHECK (
                    (num_nonnulls(alert_version, plan_id, plan_revision) = 0) OR
                    (num_nonnulls(alert_version, plan_id, plan_revision) = 3 AND alert_version > 0));
                ALTER TABLE escalation_events ADD CONSTRAINT fk_escalation_event_exact_run FOREIGN KEY (run_id, organization_id, alert_id, alert_version)
                    REFERENCES escalation_runs (id, organization_id, alert_id, alert_version);
                ALTER TABLE escalation_consumed_signals ADD CONSTRAINT fk_escalation_signal_exact_run FOREIGN KEY (run_id, organization_id, alert_id, alert_version)
                    REFERENCES escalation_runs (id, organization_id, alert_id, alert_version);
                ALTER TABLE recipient_responses ADD CONSTRAINT uq_response_escalation_scope UNIQUE (id, organization_id, alert_id, alert_version);
                ALTER TABLE alert_recipient_selections ADD CONSTRAINT uq_selection_escalation_scope UNIQUE (id, organization_id, alert_id, alert_version);
                ALTER TABLE escalation_consumed_signals ADD CONSTRAINT fk_escalation_signal_exact_response FOREIGN KEY (response_id, organization_id, alert_id, alert_version)
                    REFERENCES recipient_responses (id, organization_id, alert_id, alert_version);
                ALTER TABLE escalation_consumed_signals ADD CONSTRAINT fk_escalation_signal_exact_selection FOREIGN KEY (recipient_selection_id, organization_id, alert_id, alert_version)
                    REFERENCES alert_recipient_selections (id, organization_id, alert_id, alert_version);
                ALTER TABLE escalation_events ADD CONSTRAINT fk_escalation_event_exact_selection FOREIGN KEY (recipient_selection_id, organization_id, alert_id, alert_version)
                    REFERENCES alert_recipient_selections (id, organization_id, alert_id, alert_version);
                CREATE FUNCTION validate_escalation_signal_source() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM recipient_responses r JOIN alert_recipient_selections s
                            ON s.id = NEW.recipient_selection_id AND s.organization_id = NEW.organization_id
                            AND s.alert_id = NEW.alert_id AND s.alert_version = NEW.alert_version
                        WHERE r.id = NEW.response_id AND r.organization_id = NEW.organization_id
                            AND r.alert_id = NEW.alert_id AND r.alert_version = NEW.alert_version
                            AND (r.practitioner_id <> s.practitioner_id OR r.response_type NOT IN ('Declined', 'Unavailable')
                                OR s.selected_at_utc > r.occurred_at_utc OR r.occurred_at_utc > NEW.consumed_at_utc)) THEN
                        RAISE EXCEPTION 'Escalation signal requires active exact recipient response evidence' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER escalation_signal_source BEFORE INSERT ON escalation_consumed_signals
                    FOR EACH ROW EXECUTE FUNCTION validate_escalation_signal_source();
                CREATE FUNCTION reject_escalation_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Escalation history is append only' USING ERRCODE = '23514';
                END; $$;
                CREATE TRIGGER immutable_escalation_events BEFORE UPDATE OR DELETE ON escalation_events
                    FOR EACH ROW EXECUTE FUNCTION reject_escalation_history_mutation();
                CREATE TRIGGER immutable_escalation_events_truncate BEFORE TRUNCATE ON escalation_events
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_escalation_history_mutation();
                CREATE TRIGGER immutable_escalation_signals BEFORE UPDATE OR DELETE ON escalation_consumed_signals
                    FOR EACH ROW EXECUTE FUNCTION reject_escalation_history_mutation();
                CREATE TRIGGER immutable_escalation_signals_truncate BEFORE TRUNCATE ON escalation_consumed_signals
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_escalation_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE escalation_runs DROP CONSTRAINT fk_escalation_run_exact_plan;
                ALTER TABLE escalation_runs DROP CONSTRAINT ck_escalation_run_binding;
                ALTER TABLE escalation_events DROP CONSTRAINT fk_escalation_event_exact_run;
                ALTER TABLE escalation_events DROP CONSTRAINT fk_escalation_event_exact_selection;
                ALTER TABLE escalation_consumed_signals DROP CONSTRAINT fk_escalation_signal_exact_run;
                ALTER TABLE escalation_consumed_signals DROP CONSTRAINT fk_escalation_signal_exact_response;
                ALTER TABLE escalation_consumed_signals DROP CONSTRAINT fk_escalation_signal_exact_selection;
                ALTER TABLE escalation_runs DROP CONSTRAINT uq_escalation_run_scope;
                ALTER TABLE alert_escalation_plans DROP CONSTRAINT uq_escalation_plan_exact;
                ALTER TABLE recipient_responses DROP CONSTRAINT uq_response_escalation_scope;
                ALTER TABLE alert_recipient_selections DROP CONSTRAINT uq_selection_escalation_scope;
                DROP TRIGGER immutable_escalation_events ON escalation_events;
                DROP TRIGGER immutable_escalation_events_truncate ON escalation_events;
                DROP TRIGGER immutable_escalation_signals ON escalation_consumed_signals;
                DROP TRIGGER immutable_escalation_signals_truncate ON escalation_consumed_signals;
                DROP FUNCTION reject_escalation_history_mutation();
                DROP TRIGGER escalation_signal_source ON escalation_consumed_signals;
                DROP FUNCTION validate_escalation_signal_source();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_escalation_runs_alert_escalation_plans_plan_id_organization~",
                table: "escalation_runs");

            migrationBuilder.DropTable(
                name: "escalation_consumed_signals");

            migrationBuilder.DropTable(
                name: "escalation_events");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_escalation_runs_id_organization_id",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_organization_id_alert_id_alert_version",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_plan_id_organization_id",
                table: "escalation_runs");

            migrationBuilder.DropIndex(
                name: "IX_escalation_runs_state_lease_expires_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "alert_version",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "failure_category",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "outcome",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "paused_at_utc",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "plan_revision",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "remaining_delay",
                table: "escalation_runs");

            migrationBuilder.DropColumn(
                name: "updated_at_utc",
                table: "escalation_runs");
        }
    }
}
