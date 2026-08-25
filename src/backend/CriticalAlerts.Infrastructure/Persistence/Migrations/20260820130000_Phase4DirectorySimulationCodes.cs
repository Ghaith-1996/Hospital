using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4DirectorySimulationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "simulation_code",
                table: "sites",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "simulation_code",
                table: "departments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE sites
                SET simulation_code = CASE name
                    WHEN 'North Wing Simulation Site' THEN 'SIM-SITE-NORTH'
                    WHEN 'Riverside Annex Simulation Site' THEN 'SIM-SITE-RIVERSIDE'
                    ELSE 'SIM-SITE-' || REPLACE(id::text, '-', '')
                END
                WHERE simulation_code IS NULL;

                UPDATE departments
                SET simulation_code = CASE name
                    WHEN 'Fictional Emergency Care' THEN 'SIM-DEPT-EMERGENCY'
                    WHEN 'Fictional Medicine' THEN 'SIM-DEPT-MEDICINE'
                    WHEN 'Fictional Surgery' THEN 'SIM-DEPT-SURGERY'
                    ELSE 'SIM-DEPT-' || REPLACE(id::text, '-', '')
                END
                WHERE simulation_code IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "simulation_code",
                table: "sites",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "simulation_code",
                table: "departments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_sites_organization_id_simulation_code",
                table: "sites",
                columns: new[] { "organization_id", "simulation_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_departments_organization_id_simulation_code",
                table: "departments",
                columns: new[] { "organization_id", "simulation_code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_sites_organization_id_simulation_code",
                table: "sites");

            migrationBuilder.DropIndex(
                name: "UX_departments_organization_id_simulation_code",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "simulation_code",
                table: "sites");

            migrationBuilder.DropColumn(
                name: "simulation_code",
                table: "departments");
        }
    }
}
