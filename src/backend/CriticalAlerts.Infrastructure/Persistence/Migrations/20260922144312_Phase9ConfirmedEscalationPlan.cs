using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9ConfirmedEscalationPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "confirmed_escalation_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    revision = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_confirmed_escalation_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_confirmed_escalation_plans_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_escalation_plans_escalation_policies_policy_id_or~",
                        columns: x => new { x.policy_id, x.organization_id },
                        principalTable: "escalation_policies",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_confirmed_escalation_plans_users_confirmed_by_user_id_organ~",
                        columns: x => new { x.confirmed_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_escalation_plans_alert_id_organization_id",
                table: "confirmed_escalation_plans",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_escalation_plans_confirmed_by_user_id_organizatio~",
                table: "confirmed_escalation_plans",
                columns: new[] { "confirmed_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_escalation_plans_organization_id_alert_id_alert_v~",
                table: "confirmed_escalation_plans",
                columns: new[] { "organization_id", "alert_id", "alert_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_confirmed_escalation_plans_policy_id_organization_id",
                table: "confirmed_escalation_plans",
                columns: new[] { "policy_id", "organization_id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION reject_escalation_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Escalation approval and history are append-only' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER confirmed_escalation_plan_immutable
                BEFORE UPDATE OR DELETE ON confirmed_escalation_plans
                FOR EACH ROW EXECUTE FUNCTION reject_escalation_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "confirmed_escalation_plans");
            migrationBuilder.Sql("DROP FUNCTION reject_escalation_history_mutation();");
        }
    }
}
