using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9PublishedPolicySteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION prevent_published_escalation_step_insert() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM confirmed_escalation_plans
                        WHERE organization_id = NEW.organization_id AND policy_id = NEW.policy_id) THEN
                        RAISE EXCEPTION 'Approved escalation policy versions cannot gain new steps' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER escalation_steps_no_published_insert BEFORE INSERT ON escalation_steps
                    FOR EACH ROW EXECUTE FUNCTION prevent_published_escalation_step_insert();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER escalation_steps_no_published_insert ON escalation_steps; DROP FUNCTION prevent_published_escalation_step_insert();");
        }
    }
}
