using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AlertId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
        builder.Property(entity => entity.SiteId).GuidId(value => new SiteId(value), id => id.Value, "site_id");
        builder.Property(entity => entity.DepartmentId).GuidId(value => new DepartmentId(value), id => id.Value, "department_id");
        builder.Property(entity => entity.CreatedByUserId).GuidId(value => new UserId(value), id => id.Value, "created_by_user_id");
        builder.Property(entity => entity.SimulationPatientReference).HasColumnName("simulation_patient_reference").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.Location).HasColumnName("location").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.UrgencyLabel).HasColumnName("urgency_label").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.SourceType).HasColumnName("source_type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.DraftVersion).HasColumnName("draft_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value)).IsRequired();
        builder.Property(entity => entity.ConfirmedDraftVersion).HasColumnName("confirmed_draft_version").HasConversion(
            version => version.HasValue ? version.Value.Value : (int?)null,
            value => value.HasValue ? new AlertDraftVersion(value.Value) : null);
        builder.Property(entity => entity.ConfirmedByUserId).HasColumnName("confirmed_by_user_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new UserId(value.Value) : null);
        builder.Property(entity => entity.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        builder.Property(entity => entity.DemoEscalationPolicyVersion).HasColumnName("demo_escalation_policy_version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.DemoNotificationPolicyVersion).HasColumnName("demo_notification_policy_version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.ResolvedByUserId).HasColumnName("resolved_by_user_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new UserId(value.Value) : null);
        builder.Property(entity => entity.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(entity => entity.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Ignore(entity => entity.PendingDispatchRequests);
        builder.Ignore(entity => entity.CurrentRecipients);
        builder.Ignore(entity => entity.HasReusableApprovalForCurrentVersion);
        MapProtected(builder, entity => entity.OriginalSource, "original_source");
        MapProtected(builder, entity => entity.Transcription, "transcription");
        MapProtected(builder, entity => entity.StructuredSuggestion, "structured_suggestion");
        MapProtected(builder, entity => entity.ApprovedMessage, "approved_message");
        builder.HasMany(entity => entity.FieldConfirmations).WithOne().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(entity => new { entity.Id, entity.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(entity => entity.RecipientSelections).WithOne().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(entity => new { entity.Id, entity.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(entity => entity.StateTransitions).WithOne().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(entity => new { entity.Id, entity.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(entity => entity.FieldConfirmations).HasField("fieldConfirmations").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(entity => entity.RecipientSelections).HasField("recipientSelections").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(entity => entity.StateTransitions).HasField("stateTransitions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(entity => new { entity.OrganizationId, entity.State, entity.CreatedAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Site>().WithMany().HasForeignKey(entity => new { entity.SiteId, entity.OrganizationId }).HasPrincipalKey(site => new { site.Id, site.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(entity => new { entity.DepartmentId, entity.OrganizationId }).HasPrincipalKey(department => new { department.Id, department.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => new { entity.CreatedByUserId, entity.OrganizationId }).HasPrincipalKey(user => new { user.Id, user.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }

    private static void MapProtected(
        EntityTypeBuilder<Alert> builder,
        System.Linq.Expressions.Expression<Func<Alert, ProtectedValue?>> navigation,
        string prefix)
    {
        builder.OwnsOne(navigation, owned =>
        {
            owned.Property(value => value.Ciphertext).HasColumnName($"{prefix}_ciphertext");
            owned.Property(value => value.KeyVersion).HasColumnName($"{prefix}_key_version").HasMaxLength(64);
            owned.Property(value => value.Purpose).HasColumnName($"{prefix}_purpose").HasMaxLength(64);
        });
        builder.Navigation(navigation).IsRequired(false);
    }
}

internal sealed class AlertFieldConfirmationConfiguration : IEntityTypeConfiguration<AlertFieldConfirmation>
{
    public void Configure(EntityTypeBuilder<AlertFieldConfirmation> builder)
    {
        builder.ToTable("alert_field_confirmations");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AlertFieldConfirmationId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.AlertVersion).HasColumnName("alert_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value));
        builder.Property(entity => entity.FieldId).HasColumnName("field_id").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.OriginalValue).HasColumnName("original_value").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.NormalizedValue).HasColumnName("normalized_value").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Unit).HasColumnName("unit").HasMaxLength(40);
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ConfirmedByUserId).GuidId(value => new UserId(value), id => id.Value, "confirmed_by_user_id");
        builder.Property(entity => entity.ConfirmedAtUtc).HasColumnName("confirmed_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.AlertId, entity.AlertVersion, entity.FieldId })
            .IsUnique()
            .HasDatabaseName("UX_alert_field_confirmations_alert_id_alert_version_field_id");
    }
}

internal sealed class AlertRecipientSelectionConfiguration : IEntityTypeConfiguration<AlertRecipientSelection>
{
    public void Configure(EntityTypeBuilder<AlertRecipientSelection> builder)
    {
        builder.ToTable("alert_recipient_selections");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AlertRecipientSelectionId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.AlertVersion).HasColumnName("alert_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value));
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.PractitionerRoleId).HasColumnName("practitioner_role_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new PractitionerRoleId(value.Value) : null);
        builder.Property(entity => entity.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.SelectedByUserId).GuidId(value => new UserId(value), id => id.Value, "selected_by_user_id");
        builder.Property(entity => entity.SelectedAtUtc).HasColumnName("selected_at_utc").IsRequired();
        builder.Property(entity => entity.DirectoryRevision).HasColumnName("directory_revision").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.DirectorySourceUpdatedAtUtc).HasColumnName("directory_source_updated_at_utc");
        builder.Property(entity => entity.OnCallSnapshot).HasColumnName("on_call_snapshot").HasMaxLength(80);
        builder.HasIndex(entity => new
            {
                entity.AlertId,
                entity.AlertVersion,
                entity.PractitionerId,
                entity.Channel,
            })
            .IsUnique()
            .HasDatabaseName("UX_alert_recipient_selection_version_practitioner_channel");
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PractitionerRoleAssignment>().WithMany().HasForeignKey(entity => new { entity.PractitionerRoleId, entity.OrganizationId }).HasPrincipalKey(role => new { role.Id, role.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AlertStateTransitionConfiguration : IEntityTypeConfiguration<AlertStateTransition>
{
    public void Configure(EntityTypeBuilder<AlertStateTransition> builder)
    {
        builder.ToTable("alert_state_transitions");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AlertStateTransitionId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.FromState).HasColumnName("from_state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ToState).HasColumnName("to_state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ActorUserId).HasColumnName("actor_user_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new UserId(value.Value) : null);
        builder.Property(entity => entity.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.CorrelationId).HasColumnName("correlation_id").HasMaxLength(96).IsRequired();
        builder.Property(entity => entity.PolicyVersion).HasColumnName("policy_version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.AlertId, entity.OccurredAtUtc });
    }
}
