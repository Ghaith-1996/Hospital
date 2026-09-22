using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class ConfirmedEscalationPlanConfiguration : IEntityTypeConfiguration<ConfirmedEscalationPlan>
{
    public void Configure(EntityTypeBuilder<ConfirmedEscalationPlan> builder)
    {
        builder.ToTable("confirmed_escalation_plans");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(row => row.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(row => row.AlertVersion).HasColumnName("alert_version").HasConversion(version => version.Value, value => new AlertDraftVersion(value));
        builder.Property(row => row.PolicyId).GuidId(value => new EscalationPolicyId(value), id => id.Value, "policy_id");
        builder.Property(row => row.PolicyVersion).HasColumnName("policy_version").HasMaxLength(40);
        builder.Property(row => row.Revision).HasColumnName("revision").HasMaxLength(128);
        builder.Property(row => row.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("jsonb");
        builder.Property(row => row.ConfirmedByUserId).GuidId(value => new UserId(value), id => id.Value, "confirmed_by_user_id");
        builder.Property(row => row.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
        builder.HasIndex(row => new { row.OrganizationId, row.AlertId, row.AlertVersion }).IsUnique();
        builder.HasOne<Alert>().WithMany().HasForeignKey(row => new { row.AlertId, row.OrganizationId })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EscalationPolicy>().WithMany().HasForeignKey(row => new { row.PolicyId, row.OrganizationId })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(row => new { row.ConfirmedByUserId, row.OrganizationId })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
