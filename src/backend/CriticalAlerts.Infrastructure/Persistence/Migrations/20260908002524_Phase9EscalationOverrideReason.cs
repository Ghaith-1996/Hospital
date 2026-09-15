using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9EscalationOverrideReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "override_reason",
                table: "escalation_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
            // Historical events retain NULL; never invent a human reason or backfill runs.
            migrationBuilder.Sql("""
                ALTER TABLE escalation_events ADD CONSTRAINT ck_escalation_override_reason CHECK (
                    override_reason IS NULL OR
                    (event_type = 'Paused' AND override_reason IN ('OperatorReview', 'ManualCoordination')) OR
                    (event_type = 'Resumed' AND override_reason = 'ReadyToResume'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE escalation_events DROP CONSTRAINT ck_escalation_override_reason;");
            migrationBuilder.DropColumn(
                name: "override_reason",
                table: "escalation_events");
        }
    }
}
