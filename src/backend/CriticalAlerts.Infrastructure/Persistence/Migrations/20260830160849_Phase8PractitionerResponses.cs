using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8PractitionerResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recipient_responses_alert_recipient_selections_recipient_se~",
                table: "recipient_responses");

            migrationBuilder.RenameColumn(
                name: "recipient_selection_id",
                table: "recipient_responses",
                newName: "practitioner_id");

            migrationBuilder.RenameIndex(
                name: "IX_recipient_responses_recipient_selection_id_organization_id",
                table: "recipient_responses",
                newName: "IX_recipient_responses_practitioner_id_organization_id");

            migrationBuilder.AddColumn<int>(
                name: "alert_version",
                table: "responsibility_assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "source_response_id",
                table: "responsibility_assignments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<int>(
                name: "alert_version",
                table: "recipient_responses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "response_category",
                table: "recipient_responses",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "opened_at_utc",
                table: "delivery_attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_recipient_responses_id_organization_id",
                table: "recipient_responses",
                columns: new[] { "id", "organization_id" });

            migrationBuilder.CreateTable(
                name: "practitioner_user_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practitioner_user_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_practitioner_user_links_practitioners_practitioner_id_organ~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_practitioner_user_links_users_user_id_organization_id",
                        columns: x => new { x.user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_responsibility_assignments_organization_id_alert_id_alert_v~",
                table: "responsibility_assignments",
                columns: new[] { "organization_id", "alert_id", "alert_version", "practitioner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_responsibility_assignments_organization_id_source_response_~",
                table: "responsibility_assignments",
                columns: new[] { "organization_id", "source_response_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_responsibility_assignments_practitioner_id_organization_id",
                table: "responsibility_assignments",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_responsibility_assignments_source_response_id_organization_~",
                table: "responsibility_assignments",
                columns: new[] { "source_response_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "UX_recipient_responses_practitioner_category",
                table: "recipient_responses",
                columns: new[] { "organization_id", "alert_id", "alert_version", "practitioner_id", "response_category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_user_links_organization_id_practitioner_id",
                table: "practitioner_user_links",
                columns: new[] { "organization_id", "practitioner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_user_links_organization_id_user_id",
                table: "practitioner_user_links",
                columns: new[] { "organization_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_user_links_practitioner_id_organization_id",
                table: "practitioner_user_links",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_user_links_user_id_organization_id",
                table: "practitioner_user_links",
                columns: new[] { "user_id", "organization_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_recipient_responses_practitioners_practitioner_id_organizat~",
                table: "recipient_responses",
                columns: new[] { "practitioner_id", "organization_id" },
                principalTable: "practitioners",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_responsibility_assignments_practitioners_practitioner_id_or~",
                table: "responsibility_assignments",
                columns: new[] { "practitioner_id", "organization_id" },
                principalTable: "practitioners",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_responsibility_assignments_recipient_responses_source_respo~",
                table: "responsibility_assignments",
                columns: new[] { "source_response_id", "organization_id" },
                principalTable: "recipient_responses",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recipient_responses_practitioners_practitioner_id_organizat~",
                table: "recipient_responses");

            migrationBuilder.DropForeignKey(
                name: "FK_responsibility_assignments_practitioners_practitioner_id_or~",
                table: "responsibility_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_responsibility_assignments_recipient_responses_source_respo~",
                table: "responsibility_assignments");

            migrationBuilder.DropTable(
                name: "practitioner_user_links");

            migrationBuilder.DropIndex(
                name: "IX_responsibility_assignments_organization_id_alert_id_alert_v~",
                table: "responsibility_assignments");

            migrationBuilder.DropIndex(
                name: "IX_responsibility_assignments_organization_id_source_response_~",
                table: "responsibility_assignments");

            migrationBuilder.DropIndex(
                name: "IX_responsibility_assignments_practitioner_id_organization_id",
                table: "responsibility_assignments");

            migrationBuilder.DropIndex(
                name: "IX_responsibility_assignments_source_response_id_organization_~",
                table: "responsibility_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_recipient_responses_id_organization_id",
                table: "recipient_responses");

            migrationBuilder.DropIndex(
                name: "UX_recipient_responses_practitioner_category",
                table: "recipient_responses");

            migrationBuilder.DropColumn(
                name: "alert_version",
                table: "responsibility_assignments");

            migrationBuilder.DropColumn(
                name: "source_response_id",
                table: "responsibility_assignments");

            migrationBuilder.DropColumn(
                name: "alert_version",
                table: "recipient_responses");

            migrationBuilder.DropColumn(
                name: "response_category",
                table: "recipient_responses");

            migrationBuilder.DropColumn(
                name: "opened_at_utc",
                table: "delivery_attempts");

            migrationBuilder.RenameColumn(
                name: "practitioner_id",
                table: "recipient_responses",
                newName: "recipient_selection_id");

            migrationBuilder.RenameIndex(
                name: "IX_recipient_responses_practitioner_id_organization_id",
                table: "recipient_responses",
                newName: "IX_recipient_responses_recipient_selection_id_organization_id");

            migrationBuilder.AddForeignKey(
                name: "FK_recipient_responses_alert_recipient_selections_recipient_se~",
                table: "recipient_responses",
                columns: new[] { "recipient_selection_id", "organization_id" },
                principalTable: "alert_recipient_selections",
                principalColumns: new[] { "id", "organization_id" },
                onDelete: ReferentialAction.Restrict);
        }
    }
}
