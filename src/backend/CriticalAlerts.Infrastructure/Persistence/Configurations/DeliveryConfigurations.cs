using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new DeliveryAttemptId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.RecipientSelectionId).GuidId(value => new AlertRecipientSelectionId(value), id => id.Value, "recipient_selection_id");
        builder.Property(entity => entity.Channel).HasColumnName("channel").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Provider).HasColumnName("provider").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.ProviderReference).HasColumnName("provider_reference").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.OpenedState).HasColumnName("opened_state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.OpenedAtUtc).HasColumnName("opened_at_utc");
        builder.Property(entity => entity.RequestedAtUtc).HasColumnName("requested_at_utc").IsRequired();
        builder.Property(entity => entity.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(entity => entity.DeliveredAtUtc).HasColumnName("delivered_at_utc");
        builder.Property(entity => entity.FailedAtUtc).HasColumnName("failed_at_utc");
        builder.Property(entity => entity.FailureCategory).HasColumnName("failure_category").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique();
        builder.HasOne<Alert>().WithMany().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(alert => new { alert.Id, alert.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertRecipientSelection>().WithMany().HasForeignKey(entity => new { entity.RecipientSelectionId, entity.OrganizationId }).HasPrincipalKey(recipient => new { recipient.Id, recipient.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliveryEventConfiguration : IEntityTypeConfiguration<DeliveryEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryEvent> builder)
    {
        builder.ToTable("delivery_events");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new DeliveryEventId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.DeliveryAttemptId).GuidId(value => new DeliveryAttemptId(value), id => id.Value, "delivery_attempt_id");
        builder.Property(entity => entity.EventType).HasColumnName("event_type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(entity => entity.SanitizedMetadata).HasColumnName("sanitized_metadata").HasMaxLength(500).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.ProviderEventId }).IsUnique();
        builder.HasOne<DeliveryAttempt>().WithMany().HasForeignKey(entity => new { entity.DeliveryAttemptId, entity.OrganizationId }).HasPrincipalKey(attempt => new { attempt.Id, attempt.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecipientResponseConfiguration : IEntityTypeConfiguration<RecipientResponse>
{
    public void Configure(EntityTypeBuilder<RecipientResponse> builder)
    {
        builder.ToTable("recipient_responses");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new RecipientResponseId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.AlertVersion).HasColumnName("alert_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value)).IsRequired();
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.ResponseType).HasColumnName("response_type").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.Category).HasColumnName("response_category").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ActorUserId).GuidId(value => new UserId(value), id => id.Value, "actor_user_id");
        builder.Property(entity => entity.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(entity => entity.SanitizedReasonCode).HasColumnName("sanitized_reason_code").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => new
            {
                entity.OrganizationId,
                entity.AlertId,
                entity.AlertVersion,
                entity.PractitionerId,
                entity.Category,
            })
            .IsUnique()
            .HasDatabaseName("UX_recipient_responses_practitioner_category");
        builder.HasOne<Alert>().WithMany().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(alert => new { alert.Id, alert.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ResponsibilityAssignmentConfiguration : IEntityTypeConfiguration<ResponsibilityAssignment>
{
    public void Configure(EntityTypeBuilder<ResponsibilityAssignment> builder)
    {
        builder.ToTable("responsibility_assignments");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new ResponsibilityAssignmentId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.AlertVersion).HasColumnName("alert_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value)).IsRequired();
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.ActorUserId).GuidId(value => new UserId(value), id => id.Value, "actor_user_id");
        builder.Property(entity => entity.SourceResponseId).GuidId(value => new RecipientResponseId(value), id => id.Value, "source_response_id");
        builder.Property(entity => entity.AcceptedAtUtc).HasColumnName("accepted_at_utc").IsRequired();
        builder.Property(entity => entity.ReleasedAtUtc).HasColumnName("released_at_utc");
        builder.Property(entity => entity.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.AlertId, entity.AlertVersion, entity.PractitionerId }).IsUnique();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.SourceResponseId }).IsUnique();
        builder.HasOne<Alert>().WithMany().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(alert => new { alert.Id, alert.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecipientResponse>().WithMany().HasForeignKey(entity => new { entity.SourceResponseId, entity.OrganizationId }).HasPrincipalKey(response => new { response.Id, response.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EscalationRunConfiguration : IEntityTypeConfiguration<EscalationRun>
{
    public void Configure(EntityTypeBuilder<EscalationRun> builder)
    {
        builder.ToTable("escalation_runs");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new EscalationRunId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(entity => entity.PolicyId).GuidId(value => new EscalationPolicyId(value), id => id.Value, "policy_id");
        builder.Property(entity => entity.PolicyVersion).HasColumnName("policy_version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.CurrentStep).HasColumnName("current_step").IsRequired();
        builder.Property(entity => entity.NextDueAtUtc).HasColumnName("next_due_at_utc").IsRequired();
        builder.Property(entity => entity.State).HasColumnName("state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(entity => entity.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.HasIndex(entity => new { entity.State, entity.NextDueAtUtc });
        builder.HasOne<Alert>().WithMany().HasForeignKey(entity => new { entity.AlertId, entity.OrganizationId }).HasPrincipalKey(alert => new { alert.Id, alert.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
