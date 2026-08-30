using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7SimulatedDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_delivery_events_provider_event_id",
                table: "delivery_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at_utc",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lease_owner",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "occurred_at_utc",
                table: "delivery_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE delivery_events SET occurred_at_utc = received_at_utc WHERE occurred_at_utc IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "occurred_at_utc",
                table: "delivery_events",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "simulation_dispatch_scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scenario = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_dispatch_scenarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_simulation_dispatch_scenarios_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_simulation_dispatch_scenarios_users_updated_by_user_id_orga~",
                        columns: x => new { x.updated_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_processing_state_next_attempt_at_utc_lease_~",
                table: "outbox_messages",
                columns: new[] { "processing_state", "next_attempt_at_utc", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_events_organization_id_provider_event_id",
                table: "delivery_events",
                columns: new[] { "organization_id", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_dispatch_scenarios_organization_id_channel",
                table: "simulation_dispatch_scenarios",
                columns: new[] { "organization_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_simulation_dispatch_scenarios_updated_by_user_id_organizati~",
                table: "simulation_dispatch_scenarios",
                columns: new[] { "updated_by_user_id", "organization_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "simulation_dispatch_scenarios");

            migrationBuilder.DropIndex(
                name: "IX_outbox_messages_processing_state_next_attempt_at_utc_lease_~",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_delivery_events_organization_id_provider_event_id",
                table: "delivery_events");

            migrationBuilder.DropColumn(
                name: "lease_expires_at_utc",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lease_owner",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "occurred_at_utc",
                table: "delivery_events");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_events_provider_event_id",
                table: "delivery_events",
                column: "provider_event_id",
                unique: true);
        }
    }
}
