using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9EscalationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "exact_escalation_plan_id",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exact_escalation_plan_revision",
                table: "alerts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exact_escalation_policy_id",
                table: "alerts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exact_escalation_policy_version",
                table: "alerts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "alert_escalation_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    escalation_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    definition_json = table.Column<string>(type: "text", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_escalation_plans", x => x.id);
                    table.UniqueConstraint("AK_alert_escalation_plans_id_organization_id", x => new { x.id, x.organization_id });
                    table.UniqueConstraint("AK_alert_escalation_plans_id_organization_id_alert_id_alert_ve~", x => new { x.id, x.organization_id, x.alert_id, x.alert_version });
                    table.ForeignKey(
                        name: "FK_alert_escalation_plans_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_plans_escalation_policies_escalation_polic~",
                        columns: x => new { x.escalation_policy_id, x.organization_id },
                        principalTable: "escalation_policies",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_plans_users_confirmed_by_user_id_organizat~",
                        columns: x => new { x.confirmed_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_escalation_recipient_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    escalation_policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    escalation_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    plan_revision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    step_sequence = table.Column<int>(type: "integer", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    directory_revision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    directory_source_updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    on_call_snapshot = table.Column<string>(type: "text", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_escalation_recipient_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_alert_escalation_plans~",
                        columns: x => new { x.plan_id, x.organization_id, x.alert_id, x.alert_version },
                        principalTable: "alert_escalation_plans",
                        principalColumns: new[] { "id", "organization_id", "alert_id", "alert_version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_alerts_alert_id_organi~",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_escalation_policies_es~",
                        columns: x => new { x.escalation_policy_id, x.organization_id },
                        principalTable: "escalation_policies",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_practitioner_roles_pra~",
                        columns: x => new { x.practitioner_role_id, x.organization_id },
                        principalTable: "practitioner_roles",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_practitioners_practiti~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_escalation_recipient_snapshots_users_confirmed_by_use~",
                        columns: x => new { x.confirmed_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_exact_escalation_plan_id_organization_id",
                table: "alerts",
                columns: new[] { "exact_escalation_plan_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_plans_alert_id_organization_id",
                table: "alert_escalation_plans",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_plans_confirmed_by_user_id_organization_id",
                table: "alert_escalation_plans",
                columns: new[] { "confirmed_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_plans_escalation_policy_id_organization_id",
                table: "alert_escalation_plans",
                columns: new[] { "escalation_policy_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_plans_organization_id_alert_id_alert_versi~",
                table: "alert_escalation_plans",
                columns: new[] { "organization_id", "alert_id", "alert_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_alert_id_organization_~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_confirmed_by_user_id_o~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "confirmed_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_escalation_policy_id_o~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "escalation_policy_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_organization_id_alert_~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "organization_id", "alert_id", "alert_version", "practitioner_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_plan_id_organization_i~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "plan_id", "organization_id", "alert_id", "alert_version" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_practitioner_id_organi~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_escalation_recipient_snapshots_practitioner_role_id_o~",
                table: "alert_escalation_recipient_snapshots",
                columns: new[] { "practitioner_role_id", "organization_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_alerts_alert_escalation_plans_exact_escalation_plan_id_orga~",
                table: "alerts",
                columns: new[] { "exact_escalation_plan_id", "organization_id" },
                principalTable: "alert_escalation_plans",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_escalation_snapshot_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Confirmed escalation snapshots are immutable' USING ERRCODE = '23514';
                END; $$;
                CREATE TRIGGER immutable_escalation_plans_truncate BEFORE TRUNCATE ON alert_escalation_plans
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_escalation_snapshot_mutation();
                CREATE TRIGGER immutable_escalation_recipients_truncate BEFORE TRUNCATE ON alert_escalation_recipient_snapshots
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_escalation_snapshot_mutation();
                CREATE TRIGGER immutable_escalation_plans BEFORE UPDATE OR DELETE ON alert_escalation_plans
                    FOR EACH ROW EXECUTE FUNCTION reject_escalation_snapshot_mutation();
                CREATE TRIGGER immutable_escalation_recipients BEFORE UPDATE OR DELETE ON alert_escalation_recipient_snapshots
                    FOR EACH ROW EXECUTE FUNCTION reject_escalation_snapshot_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER immutable_escalation_recipients_truncate ON alert_escalation_recipient_snapshots; DROP TRIGGER immutable_escalation_plans_truncate ON alert_escalation_plans; DROP TRIGGER immutable_escalation_recipients ON alert_escalation_recipient_snapshots; DROP TRIGGER immutable_escalation_plans ON alert_escalation_plans; DROP FUNCTION reject_escalation_snapshot_mutation();");
            migrationBuilder.DropForeignKey(
                name: "FK_alerts_alert_escalation_plans_exact_escalation_plan_id_orga~",
                table: "alerts");

            migrationBuilder.DropTable(
                name: "alert_escalation_recipient_snapshots");

            migrationBuilder.DropTable(
                name: "alert_escalation_plans");

            migrationBuilder.DropIndex(
                name: "IX_alerts_exact_escalation_plan_id_organization_id",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "exact_escalation_plan_id",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "exact_escalation_plan_revision",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "exact_escalation_policy_id",
                table: "alerts");

            migrationBuilder.DropColumn(
                name: "exact_escalation_policy_version",
                table: "alerts");
        }
    }
}
