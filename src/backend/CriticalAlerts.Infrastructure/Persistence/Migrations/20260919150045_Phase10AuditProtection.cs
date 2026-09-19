using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase10AuditProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_audit_history_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit history is append only' USING ERRCODE = '23514';
                END; $$;
                CREATE TRIGGER immutable_audit_events BEFORE UPDATE OR DELETE OR TRUNCATE ON audit_events
                    FOR EACH STATEMENT EXECUTE FUNCTION reject_audit_history_mutation();
                """);
            migrationBuilder.DropIndex(
                name: "IX_audit_events_organization_id_occurred_at_utc",
                table: "audit_events");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_action_occurred_at_utc_id",
                table: "audit_events",
                columns: new[] { "organization_id", "action", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_correlation_id_occurred_at_utc~",
                table: "audit_events",
                columns: new[] { "organization_id", "correlation_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_occurred_at_utc_id",
                table: "audit_events",
                columns: new[] { "organization_id", "occurred_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_resource_type_occurred_at_utc_~",
                table: "audit_events",
                columns: new[] { "organization_id", "resource_type", "occurred_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER immutable_audit_events ON audit_events;
                DROP FUNCTION reject_audit_history_mutation();
                """);
            migrationBuilder.DropIndex(
                name: "IX_audit_events_organization_id_action_occurred_at_utc_id",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_organization_id_correlation_id_occurred_at_utc~",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_organization_id_occurred_at_utc_id",
                table: "audit_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_organization_id_resource_type_occurred_at_utc_~",
                table: "audit_events");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "organization_id", "occurred_at_utc" });
        }
    }
}
