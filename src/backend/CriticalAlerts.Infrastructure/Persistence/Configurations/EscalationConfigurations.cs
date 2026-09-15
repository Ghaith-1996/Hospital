using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Escalation;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class AlertEscalationPlanConfiguration : IEntityTypeConfiguration<AlertEscalationPlan>
{
    public void Configure(EntityTypeBuilder<AlertEscalationPlan> builder)
    {
        builder.ToTable("alert_escalation_plans");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).GuidId(v => new AlertEscalationPlanId(v), id => id.Value, "id");
        builder.Property(e => e.OrganizationId).GuidId(v => new OrganizationId(v), id => id.Value, "organization_id");
        builder.Property(e => e.AlertId).GuidId(v => new AlertId(v), id => id.Value, "alert_id");
        builder.Property(e => e.EscalationPolicyId).GuidId(v => new EscalationPolicyId(v), id => id.Value, "escalation_policy_id");
        builder.Property(e => e.ConfirmedByUserId).GuidId(v => new UserId(v), id => id.Value, "confirmed_by_user_id");
        builder.Property(e => e.EscalationPolicyVersion).HasColumnName("escalation_policy_version").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Revision).HasColumnName("revision").HasMaxLength(64).IsRequired();
        builder.Property(e => e.DefinitionJson).HasColumnName("definition_json").IsRequired();
        builder.Property(e => e.AlertVersion).HasColumnName("alert_version");
        builder.Property(e => e.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        builder.Ignore(e => e.Definition);
        builder.HasAlternateKey(e => new { e.Id, e.OrganizationId });
        builder.HasAlternateKey(e => new { e.Id, e.OrganizationId, e.AlertId, e.AlertVersion });
        builder.HasIndex(e => new { e.OrganizationId, e.AlertId, e.AlertVersion }).IsUnique();
        builder.HasOne<Alert>().WithMany().HasForeignKey(e => new { e.AlertId, e.OrganizationId })
            .HasPrincipalKey(a => new { a.Id, a.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EscalationPolicy>().WithMany().HasForeignKey(e => new { e.EscalationPolicyId, e.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(e => new { e.ConfirmedByUserId, e.OrganizationId })
            .HasPrincipalKey(u => new { u.Id, u.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AlertEscalationRecipientSnapshotConfiguration : IEntityTypeConfiguration<AlertEscalationRecipientSnapshot>
{
    public void Configure(EntityTypeBuilder<AlertEscalationRecipientSnapshot> builder)
    {
        builder.ToTable("alert_escalation_recipient_snapshots");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).GuidId(v => new AlertEscalationRecipientSnapshotId(v), id => id.Value, "id");
        builder.Property(e => e.OrganizationId).GuidId(v => new OrganizationId(v), id => id.Value, "organization_id");
        builder.Property(e => e.AlertId).GuidId(v => new AlertId(v), id => id.Value, "alert_id");
        builder.Property(e => e.EscalationPolicyId).GuidId(v => new EscalationPolicyId(v), id => id.Value, "escalation_policy_id");
        builder.Property(e => e.ConfirmedByUserId).GuidId(v => new UserId(v), id => id.Value, "confirmed_by_user_id");
        builder.Property(e => e.PlanId).GuidId(v => new AlertEscalationPlanId(v), id => id.Value, "plan_id");
        builder.Property(e => e.PractitionerId).GuidId(v => new PractitionerId(v), id => id.Value, "practitioner_id");
        builder.Property(e => e.PractitionerRoleId).GuidId(v => new PractitionerRoleId(v), id => id.Value, "practitioner_role_id");
        builder.Property(e => e.EscalationPolicyVersion).HasColumnName("escalation_policy_version").HasMaxLength(40).IsRequired();
        builder.Property(e => e.PlanRevision).HasColumnName("plan_revision").HasMaxLength(64).IsRequired();
        builder.Property(e => e.DirectoryRevision).HasColumnName("directory_revision").HasMaxLength(128).IsRequired();
        builder.Property(e => e.OnCallSnapshot).HasColumnName("on_call_snapshot").IsRequired();
        builder.Property(e => e.AlertVersion).HasColumnName("alert_version");
        builder.Property(e => e.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        builder.Property(e => e.StepSequence).HasColumnName("step_sequence");
        builder.Property(e => e.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.DirectorySourceUpdatedAtUtc).HasColumnName("directory_source_updated_at_utc");
        builder.HasIndex(e => new { e.OrganizationId, e.AlertId, e.AlertVersion, e.PractitionerId, e.Channel }).IsUnique();
        builder.HasOne<AlertEscalationPlan>().WithMany().HasForeignKey(e => new { e.PlanId, e.OrganizationId, e.AlertId, e.AlertVersion })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId, p.AlertId, p.AlertVersion }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(e => new { e.PractitionerId, e.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PractitionerRoleAssignment>().WithMany().HasForeignKey(e => new { e.PractitionerRoleId, e.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Alert>().WithMany().HasForeignKey(e => new { e.AlertId, e.OrganizationId })
            .HasPrincipalKey(a => new { a.Id, a.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EscalationPolicy>().WithMany().HasForeignKey(e => new { e.EscalationPolicyId, e.OrganizationId })
            .HasPrincipalKey(p => new { p.Id, p.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(e => new { e.ConfirmedByUserId, e.OrganizationId })
            .HasPrincipalKey(u => new { u.Id, u.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EscalationConsumedSignalConfiguration : IEntityTypeConfiguration<EscalationConsumedSignal>
{
    public void Configure(EntityTypeBuilder<EscalationConsumedSignal> builder)
    {
        builder.ToTable("escalation_consumed_signals");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).GuidId(v => new EscalationConsumedSignalId(v), id => id.Value, "id");
        builder.Property(e => e.OrganizationId).GuidId(v => new OrganizationId(v), id => id.Value, "organization_id");
        builder.Property(e => e.RunId).GuidId(v => new EscalationRunId(v), id => id.Value, "run_id");
        builder.Property(e => e.AlertId).GuidId(v => new AlertId(v), id => id.Value, "alert_id");
        builder.Property(e => e.AlertVersion).HasColumnName("alert_version").HasConversion(v => v.Value, v => new AlertDraftVersion(v));
        builder.Property(e => e.ResponseId).GuidId(v => new RecipientResponseId(v), id => id.Value, "response_id");
        builder.Property(e => e.RecipientSelectionId).GuidId(v => new AlertRecipientSelectionId(v), id => id.Value, "recipient_selection_id");
        builder.Property(e => e.StepSequence).HasColumnName("step_sequence");
        builder.Property(e => e.ConsumedAtUtc).HasColumnName("consumed_at_utc");
        builder.HasIndex(e => new { e.OrganizationId, e.RunId, e.ResponseId }).IsUnique();
        builder.HasIndex(e => new { e.OrganizationId, e.RunId, e.StepSequence }).IsUnique();
        builder.HasOne<CriticalAlerts.Domain.Delivery.RecipientResponse>().WithMany().HasForeignKey(e => new { e.ResponseId, e.OrganizationId }).HasPrincipalKey(e => new { e.Id, e.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertRecipientSelection>().WithMany().HasForeignKey(e => new { e.RecipientSelectionId, e.OrganizationId }).HasPrincipalKey(e => new { e.Id, e.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EscalationEventConfiguration : IEntityTypeConfiguration<EscalationEvent>
{
    public void Configure(EntityTypeBuilder<EscalationEvent> builder)
    {
        builder.ToTable("escalation_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).GuidId(v => new EscalationEventId(v), id => id.Value, "id");
        builder.Property(e => e.OrganizationId).GuidId(v => new OrganizationId(v), id => id.Value, "organization_id");
        builder.Property(e => e.RunId).GuidId(v => new EscalationRunId(v), id => id.Value, "run_id");
        builder.Property(e => e.AlertId).GuidId(v => new AlertId(v), id => id.Value, "alert_id");
        builder.Property(e => e.AlertVersion).HasColumnName("alert_version").HasConversion(v => v.Value, v => new AlertDraftVersion(v));
        builder.Property(e => e.RecipientSelectionId).HasColumnName("recipient_selection_id").HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new AlertRecipientSelectionId(v.Value) : null);
        builder.Property(e => e.ActorUserId).HasColumnName("actor_user_id").HasConversion(v => v.HasValue ? v.Value.Value : (Guid?)null, v => v.HasValue ? new UserId(v.Value) : null);
        builder.Property(e => e.StepSequence).HasColumnName("step_sequence");
        builder.Property(e => e.EventType).HasColumnName("event_type").HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.FailureCategory).HasColumnName("failure_category").HasConversion<string>().HasMaxLength(64);
        builder.Property(e => e.OverrideReason).HasColumnName("override_reason").HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id");
        builder.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.HasIndex(e => new { e.OrganizationId, e.RunId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.OrganizationId, e.RunId, e.StepSequence, e.EventType }).IsUnique().HasFilter("event_type NOT IN ('Paused', 'Resumed', 'RecipientActivated')");
        builder.HasIndex(e => new { e.OrganizationId, e.RunId, e.StepSequence, e.RecipientSelectionId }).IsUnique().HasFilter("event_type = 'RecipientActivated'");
        builder.HasOne<CriticalAlerts.Domain.Delivery.EscalationRun>().WithMany().HasForeignKey(e => new { e.RunId, e.OrganizationId }).HasPrincipalKey(e => new { e.Id, e.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(e => new { e.ActorUserId, e.OrganizationId }).HasPrincipalKey(e => new { e.Id, e.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertRecipientSelection>().WithMany().HasForeignKey(e => new { e.RecipientSelectionId, e.OrganizationId }).HasPrincipalKey(e => new { e.Id, e.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
