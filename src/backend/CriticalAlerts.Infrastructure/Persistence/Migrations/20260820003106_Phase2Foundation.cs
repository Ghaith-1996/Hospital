using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CriticalAlerts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Foundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_simulation = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alert_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    schema_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_templates", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_templates_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    sanitized_metadata = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "directory_source_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sync_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_stale = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directory_source_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_directory_source_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "directory_sync_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    inserted_count = table.Column<int>(type: "integer", nullable: false),
                    updated_count = table.Column<int>(type: "integer", nullable: false),
                    deactivated_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    error_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_directory_sync_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_directory_sync_runs_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalation_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_condition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stop_condition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_policies", x => x.id);
                    table.UniqueConstraint("AK_escalation_policies_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_escalation_policies_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_idempotency_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    handler = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    result = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_inbox_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    allowed_channels = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    generic_sms_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    generic_voice_template = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    retry_limit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_policies", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_policies_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processing_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_outbox_messages_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "practitioners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    simulation_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practitioners", x => x.id);
                    table.UniqueConstraint("AK_practitioners_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_practitioners_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                    table.UniqueConstraint("AK_roles_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_roles_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sites", x => x.id);
                    table.UniqueConstraint("AK_sites_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_sites_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    simulation_handle = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.UniqueConstraint("AK_users_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_users_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalation_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    delay = table.Column<TimeSpan>(type: "interval", nullable: false),
                    recipient_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channels = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalation_steps_escalation_policies_policy_id_organization~",
                        columns: x => new { x.policy_id, x.organization_id },
                        principalTable: "escalation_policies",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    endpoint_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    endpoint_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    endpoint_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    simulation_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "FK_contact_endpoints_practitioners_practitioner_id_organizatio~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.id);
                    table.UniqueConstraint("AK_departments_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_departments_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_departments_sites_site_id_organization_id",
                        columns: x => new { x.site_id, x.organization_id },
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_identities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_identities", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_identities_users_user_id_organization_id",
                        columns: x => new { x.user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_roles", x => new { x.organization_id, x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "FK_user_roles_roles_role_id_organization_id",
                        columns: x => new { x.role_id, x.organization_id },
                        principalTable: "roles",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_roles_users_user_id_organization_id",
                        columns: x => new { x.user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    simulation_patient_reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    urgency_label = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    original_source_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    original_source_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original_source_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    transcription_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    transcription_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    transcription_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    structured_suggestion_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    structured_suggestion_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    structured_suggestion_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    approved_message_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    approved_message_key_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    approved_message_purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    draft_version = table.Column<int>(type: "integer", nullable: false),
                    confirmed_draft_version = table.Column<int>(type: "integer", nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    demo_escalation_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    demo_notification_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.id);
                    table.UniqueConstraint("AK_alerts_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_alerts_departments_department_id_organization_id",
                        columns: x => new { x.department_id, x.organization_id },
                        principalTable: "departments",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerts_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerts_sites_site_id_organization_id",
                        columns: x => new { x.site_id, x.organization_id },
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alerts_users_created_by_user_id_organization_id",
                        columns: x => new { x.created_by_user_id, x.organization_id },
                        principalTable: "users",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "on_call_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_system = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_record_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_synchronized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_on_call_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_on_call_assignments_departments_department_id_organization_~",
                        columns: x => new { x.department_id, x.organization_id },
                        principalTable: "departments",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_on_call_assignments_practitioners_practitioner_id_organizat~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_on_call_assignments_sites_site_id_organization_id",
                        columns: x => new { x.site_id, x.organization_id },
                        principalTable: "sites",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "practitioner_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practitioner_roles", x => x.id);
                    table.UniqueConstraint("AK_practitioner_roles_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_practitioner_roles_departments_department_id_organization_id",
                        columns: x => new { x.department_id, x.organization_id },
                        principalTable: "departments",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_practitioner_roles_practitioners_practitioner_id_organizati~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_field_confirmations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    field_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_field_confirmations", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_field_confirmations_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_recipient_selections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_version = table.Column<int>(type: "integer", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    selected_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    selected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_recipient_selections", x => x.id);
                    table.UniqueConstraint("AK_alert_recipient_selections_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_alert_recipient_selections_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_alert_recipient_selections_practitioners_practitioner_id_or~",
                        columns: x => new { x.practitioner_id, x.organization_id },
                        principalTable: "practitioners",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "alert_state_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_state_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_state_transitions_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "escalation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_id = table.Column<Guid>(type: "uuid", nullable: false),
                    policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    current_step = table.Column<int>(type: "integer", nullable: false),
                    next_due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_escalation_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_escalation_runs_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "responsibility_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practitioner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_responsibility_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_responsibility_assignments_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    opened_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.id);
                    table.UniqueConstraint("AK_delivery_attempts_id_organization_id", x => new { x.id, x.organization_id });
                    table.ForeignKey(
                        name: "FK_delivery_attempts_alert_recipient_selections_recipient_sele~",
                        columns: x => new { x.recipient_selection_id, x.organization_id },
                        principalTable: "alert_recipient_selections",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_delivery_attempts_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recipient_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_selection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sanitized_reason_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recipient_responses", x => x.id);
                    table.ForeignKey(
                        name: "FK_recipient_responses_alert_recipient_selections_recipient_se~",
                        columns: x => new { x.recipient_selection_id, x.organization_id },
                        principalTable: "alert_recipient_selections",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_recipient_responses_alerts_alert_id_organization_id",
                        columns: x => new { x.alert_id, x.organization_id },
                        principalTable: "alerts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "delivery_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sanitized_metadata = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_delivery_events_delivery_attempts_delivery_attempt_id_organ~",
                        columns: x => new { x.delivery_attempt_id, x.organization_id },
                        principalTable: "delivery_attempts",
                        principalColumns: new[] { "id", "organization_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_field_confirmations_alert_id_alert_version_field_id",
                table: "alert_field_confirmations",
                columns: new[] { "alert_id", "alert_version", "field_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_field_confirmations_alert_id_organization_id",
                table: "alert_field_confirmations",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_recipient_selections_alert_id_organization_id",
                table: "alert_recipient_selections",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_recipient_selections_alert_id_practitioner_id_channel",
                table: "alert_recipient_selections",
                columns: new[] { "alert_id", "practitioner_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alert_recipient_selections_practitioner_id_organization_id",
                table: "alert_recipient_selections",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_state_transitions_alert_id_occurred_at_utc",
                table: "alert_state_transitions",
                columns: new[] { "alert_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_state_transitions_alert_id_organization_id",
                table: "alert_state_transitions",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alert_templates_organization_id_version",
                table: "alert_templates",
                columns: new[] { "organization_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alerts_created_by_user_id_organization_id",
                table: "alerts",
                columns: new[] { "created_by_user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_department_id_organization_id",
                table: "alerts",
                columns: new[] { "department_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_organization_id_state_created_at_utc",
                table: "alerts",
                columns: new[] { "organization_id", "state", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_site_id_organization_id",
                table: "alerts",
                columns: new[] { "site_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_organization_id_occurred_at_utc",
                table: "audit_events",
                columns: new[] { "organization_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_contact_endpoints_practitioner_id_organization_id",
                table: "contact_endpoints",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_alert_id_organization_id",
                table: "delivery_attempts",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_idempotency_key",
                table: "delivery_attempts",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_delivery_attempts_recipient_selection_id_organization_id",
                table: "delivery_attempts",
                columns: new[] { "recipient_selection_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_events_delivery_attempt_id_organization_id",
                table: "delivery_events",
                columns: new[] { "delivery_attempt_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_events_provider_event_id",
                table: "delivery_events",
                column: "provider_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_organization_id",
                table: "departments",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_departments_site_id_organization_id",
                table: "departments",
                columns: new[] { "site_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_directory_source_records_organization_id_source_system_sour~",
                table: "directory_source_records",
                columns: new[] { "organization_id", "source_system", "source_record_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_directory_sync_runs_organization_id_started_at_utc",
                table: "directory_sync_runs",
                columns: new[] { "organization_id", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_policies_organization_id_version",
                table: "escalation_policies",
                columns: new[] { "organization_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_alert_id_organization_id",
                table: "escalation_runs",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_runs_state_next_due_at_utc",
                table: "escalation_runs",
                columns: new[] { "state", "next_due_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_steps_policy_id_organization_id",
                table: "escalation_steps",
                columns: new[] { "policy_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_escalation_steps_policy_id_sequence_number",
                table: "escalation_steps",
                columns: new[] { "policy_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identities_organization_id_provider_subject",
                table: "external_identities",
                columns: new[] { "organization_id", "provider", "subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_identities_user_id_organization_id",
                table: "external_identities",
                columns: new[] { "user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_organization_id_operation_type_idempote~",
                table: "idempotency_records",
                columns: new[] { "organization_id", "operation_type", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_external_message_id_handler",
                table: "inbox_messages",
                columns: new[] { "external_message_id", "handler" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_organization_id",
                table: "inbox_messages",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_policies_organization_id_version",
                table: "notification_policies",
                columns: new[] { "organization_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_on_call_assignments_department_id_organization_id",
                table: "on_call_assignments",
                columns: new[] { "department_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_on_call_assignments_organization_id_starts_at_utc_ends_at_u~",
                table: "on_call_assignments",
                columns: new[] { "organization_id", "starts_at_utc", "ends_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_on_call_assignments_practitioner_id_organization_id",
                table: "on_call_assignments",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_on_call_assignments_site_id_organization_id",
                table: "on_call_assignments",
                columns: new[] { "site_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_organizations_name",
                table: "organizations",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_idempotency_key",
                table: "outbox_messages",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_organization_id",
                table: "outbox_messages",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_processing_state_next_attempt_at_utc",
                table: "outbox_messages",
                columns: new[] { "processing_state", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_roles_department_id_organization_id",
                table: "practitioner_roles",
                columns: new[] { "department_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioner_roles_practitioner_id_organization_id",
                table: "practitioner_roles",
                columns: new[] { "practitioner_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_organization_id_is_active",
                table: "practitioners",
                columns: new[] { "organization_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_organization_id_last_name_first_name",
                table: "practitioners",
                columns: new[] { "organization_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "IX_practitioners_organization_id_simulation_code",
                table: "practitioners",
                columns: new[] { "organization_id", "simulation_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_recipient_responses_alert_id_organization_id",
                table: "recipient_responses",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_recipient_responses_recipient_selection_id_organization_id",
                table: "recipient_responses",
                columns: new[] { "recipient_selection_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_responsibility_assignments_alert_id_organization_id",
                table: "responsibility_assignments",
                columns: new[] { "alert_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_roles_organization_id_name",
                table: "roles",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sites_organization_id",
                table: "sites",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_role_id_organization_id",
                table: "user_roles",
                columns: new[] { "role_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_user_roles_user_id_organization_id",
                table: "user_roles",
                columns: new[] { "user_id", "organization_id" });

            migrationBuilder.CreateIndex(
                name: "IX_users_organization_id_simulation_handle",
                table: "users",
                columns: new[] { "organization_id", "simulation_handle" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_field_confirmations");

            migrationBuilder.DropTable(
                name: "alert_state_transitions");

            migrationBuilder.DropTable(
                name: "alert_templates");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "contact_endpoints");

            migrationBuilder.DropTable(
                name: "delivery_events");

            migrationBuilder.DropTable(
                name: "directory_source_records");

            migrationBuilder.DropTable(
                name: "directory_sync_runs");

            migrationBuilder.DropTable(
                name: "escalation_runs");

            migrationBuilder.DropTable(
                name: "escalation_steps");

            migrationBuilder.DropTable(
                name: "external_identities");

            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "notification_policies");

            migrationBuilder.DropTable(
                name: "on_call_assignments");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "practitioner_roles");

            migrationBuilder.DropTable(
                name: "recipient_responses");

            migrationBuilder.DropTable(
                name: "responsibility_assignments");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "delivery_attempts");

            migrationBuilder.DropTable(
                name: "escalation_policies");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "alert_recipient_selections");

            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "practitioners");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropTable(
                name: "organizations");
        }
    }
}
