using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Assistance;
using CriticalAlerts.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class AssistanceResultConfiguration : IEntityTypeConfiguration<AssistanceResult>
{
    public void Configure(EntityTypeBuilder<AssistanceResult> builder)
    {
        builder.ToTable("alert_assistance_results");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id");
        builder.Property(row => row.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(row => row.AlertId).GuidId(value => new AlertId(value), id => id.Value, "alert_id");
        builder.Property(row => row.SourceRevisionId).GuidId(value => new AlertSourceRevisionId(value), id => id.Value, "source_revision_id");
        builder.Property(row => row.RequestedByUserId).GuidId(value => new UserId(value), id => id.Value, "requested_by_user_id");
        builder.Property(row => row.AlertVersion).HasColumnName("alert_version").HasConversion(value => value.Value, value => new AlertDraftVersion(value));
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        builder.Property(row => row.Provider).HasColumnName("provider").HasMaxLength(32);
        builder.Property(row => row.ProviderVersion).HasColumnName("provider_version").HasMaxLength(40);
        builder.Property(row => row.ConfigurationVersion).HasColumnName("configuration_version").HasMaxLength(40);
        builder.Property(row => row.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.OwnsOne(row => row.Payload, owned =>
        {
            owned.Property(value => value.Ciphertext).HasColumnName("result_ciphertext");
            owned.Property(value => value.KeyVersion).HasColumnName("result_key_version").HasMaxLength(64);
            owned.Property(value => value.Purpose).HasColumnName("result_purpose").HasMaxLength(64);
        });
        builder.Navigation(row => row.Payload).IsRequired();
        builder.HasOne<Alert>().WithMany().HasForeignKey(row => new { row.AlertId, row.OrganizationId })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AlertSourceRevision>().WithMany().HasForeignKey(row => new { row.SourceRevisionId, row.OrganizationId, row.AlertId, row.AlertVersion })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId, row.AlertId, row.AlertVersion }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(row => new { row.RequestedByUserId, row.OrganizationId })
            .HasPrincipalKey(row => new { row.Id, row.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.OrganizationId, row.AlertId, row.Kind, row.CreatedAtUtc, row.Id });
    }
}
