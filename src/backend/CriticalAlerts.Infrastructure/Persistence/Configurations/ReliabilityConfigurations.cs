using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Reliability;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AuditEventId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.ActorType).HasColumnName("actor_type").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ActorUserId).HasColumnName("actor_user_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new UserId(value.Value) : null);
        builder.Property(entity => entity.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.ResourceType).HasColumnName("resource_type").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ResourceId).HasColumnName("resource_id").IsRequired();
        builder.Property(entity => entity.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.CorrelationId).HasColumnName("correlation_id").HasMaxLength(96).IsRequired();
        builder.Property(entity => entity.SanitizedMetadata).HasColumnName("sanitized_metadata").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.OccurredAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new OutboxMessageId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.AggregateId).HasColumnName("aggregate_id").IsRequired();
        builder.Property(entity => entity.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.ProcessingState).HasColumnName("processing_state").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(entity => entity.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc").IsRequired();
        builder.Property(entity => entity.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(entity => entity.LastErrorCategory).HasColumnName("last_error_category").HasMaxLength(64).IsRequired();
        builder.HasIndex(entity => entity.IdempotencyKey).IsUnique();
        builder.HasIndex(entity => new { entity.ProcessingState, entity.NextAttemptAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new InboxMessageId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.ExternalMessageId).HasColumnName("external_message_id").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Handler).HasColumnName("handler").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Result).HasColumnName("result").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ProcessedAtUtc).HasColumnName("processed_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.ExternalMessageId, entity.Handler }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new IdempotencyRecordId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.OperationType).HasColumnName("operation_type").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.RequestHash).HasColumnName("request_hash").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.ResultReference).HasColumnName("result_reference").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(entity => entity.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.HasIndex(entity => new { entity.OrganizationId, entity.OperationType, entity.IdempotencyKey }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
