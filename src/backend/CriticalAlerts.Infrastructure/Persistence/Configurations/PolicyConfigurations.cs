using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class AlertTemplateConfiguration : IEntityTypeConfiguration<AlertTemplate>
{
    public void Configure(EntityTypeBuilder<AlertTemplate> builder)
    {
        builder.ToTable("alert_templates");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new AlertTemplateId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Version).HasColumnName("version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(entity => entity.SchemaJson).HasColumnName("schema_json").HasColumnType("jsonb").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Version }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationPolicyConfiguration : IEntityTypeConfiguration<NotificationPolicy>
{
    public void Configure(EntityTypeBuilder<NotificationPolicy> builder)
    {
        builder.ToTable("notification_policies");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new NotificationPolicyId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.Version).HasColumnName("version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(entity => entity.AllowedChannels).HasColumnName("allowed_channels").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.GenericSmsTemplate).HasColumnName("generic_sms_template").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.GenericVoiceTemplate).HasColumnName("generic_voice_template").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.RetryLimit).HasColumnName("retry_limit").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Version }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EscalationPolicyConfiguration : IEntityTypeConfiguration<EscalationPolicy>
{
    public void Configure(EntityTypeBuilder<EscalationPolicy> builder)
    {
        builder.ToTable("escalation_policies");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new EscalationPolicyId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Version).HasColumnName("version").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(entity => entity.TriggerCondition).HasColumnName("trigger_condition").HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.StopCondition).HasColumnName("stop_condition").HasMaxLength(500).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Version }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class EscalationStepConfiguration : IEntityTypeConfiguration<EscalationStep>
{
    public void Configure(EntityTypeBuilder<EscalationStep> builder)
    {
        builder.ToTable("escalation_steps");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new EscalationStepId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.PolicyId).GuidId(value => new EscalationPolicyId(value), id => id.Value, "policy_id");
        builder.Property(entity => entity.SequenceNumber).HasColumnName("sequence_number").IsRequired();
        builder.Property(entity => entity.Delay).HasColumnName("delay").IsRequired();
        builder.Property(entity => entity.RecipientSource).HasColumnName("recipient_source").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.Channels).HasColumnName("channels").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.HasIndex(entity => new { entity.PolicyId, entity.SequenceNumber }).IsUnique();
        builder.HasOne<EscalationPolicy>().WithMany().HasForeignKey(entity => new { entity.PolicyId, entity.OrganizationId }).HasPrincipalKey(policy => new { policy.Id, policy.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
