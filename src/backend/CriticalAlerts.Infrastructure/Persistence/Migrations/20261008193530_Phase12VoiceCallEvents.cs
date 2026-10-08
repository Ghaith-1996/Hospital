using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase12VoiceCallEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "callback_tag",
                table: "provider_send_ledger",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "operation_fingerprint",
                table: "provider_send_ledger",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pending_provider_call_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    callback_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    call_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    end_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_provider_call_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_pending_provider_call_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_provider_send_ledger_callback_tag",
                table: "provider_send_ledger",
                columns: new[] { "provider", "callback_tag" },
                unique: true,
                filter: "callback_tag IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_pending_provider_call_events_organization_id",
                table: "pending_provider_call_events",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_pending_provider_call_events_tag",
                table: "pending_provider_call_events",
                columns: new[] { "provider", "callback_tag" });

            migrationBuilder.CreateIndex(
                name: "UX_pending_provider_call_events_event",
                table: "pending_provider_call_events",
                columns: new[] { "provider", "external_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pending_provider_call_events");

            migrationBuilder.DropIndex(
                name: "UX_provider_send_ledger_callback_tag",
                table: "provider_send_ledger");

            migrationBuilder.DropColumn(
                name: "callback_tag",
                table: "provider_send_ledger");

            migrationBuilder.DropColumn(
                name: "operation_fingerprint",
                table: "provider_send_ledger");
        }
    }
}
