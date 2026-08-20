using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class PractitionerConfiguration : IEntityTypeConfiguration<Practitioner>
{
    public void Configure(EntityTypeBuilder<Practitioner> builder)
    {
        builder.ToTable("practitioners");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new PractitionerId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.SimulationCode).HasColumnName("simulation_code").HasMaxLength(40).IsRequired();
        builder.Property(entity => entity.Specialty).HasColumnName("specialty").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.SimulationCode }).IsUnique();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.LastName, entity.FirstName });
        builder.HasIndex(entity => new { entity.OrganizationId, entity.IsActive });
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PractitionerRoleAssignmentConfiguration : IEntityTypeConfiguration<PractitionerRoleAssignment>
{
    public void Configure(EntityTypeBuilder<PractitionerRoleAssignment> builder)
    {
        builder.ToTable("practitioner_roles");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new PractitionerRoleId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.DepartmentId).GuidId(value => new DepartmentId(value), id => id.Value, "department_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.Title).HasColumnName("title").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(entity => new { entity.DepartmentId, entity.OrganizationId }).HasPrincipalKey(department => new { department.Id, department.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ContactEndpointConfiguration : IEntityTypeConfiguration<ContactEndpoint>
{
    public void Configure(EntityTypeBuilder<ContactEndpoint> builder)
    {
        builder.ToTable("contact_endpoints");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new ContactEndpointId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.SimulationLabel).HasColumnName("simulation_label").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.IsPrimary).HasColumnName("is_primary").IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.OwnsOne(entity => entity.ProtectedValue, owned =>
        {
            owned.Property(value => value.Ciphertext).HasColumnName("endpoint_ciphertext").IsRequired();
            owned.Property(value => value.KeyVersion).HasColumnName("endpoint_key_version").HasMaxLength(64).IsRequired();
            owned.Property(value => value.Purpose).HasColumnName("endpoint_purpose").HasMaxLength(64).IsRequired();
        });
        builder.Navigation(entity => entity.ProtectedValue).IsRequired();
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OnCallAssignmentConfiguration : IEntityTypeConfiguration<OnCallAssignment>
{
    public void Configure(EntityTypeBuilder<OnCallAssignment> builder)
    {
        builder.ToTable("on_call_assignments");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new OnCallAssignmentId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.PractitionerId).GuidId(value => new PractitionerId(value), id => id.Value, "practitioner_id");
        builder.Property(entity => entity.SiteId).GuidId(value => new SiteId(value), id => id.Value, "site_id");
        builder.Property(entity => entity.DepartmentId).GuidId(value => new DepartmentId(value), id => id.Value, "department_id");
        builder.Property(entity => entity.Tier).HasColumnName("tier").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.StartsAtUtc).HasColumnName("starts_at_utc").IsRequired();
        builder.Property(entity => entity.EndsAtUtc).HasColumnName("ends_at_utc").IsRequired();
        builder.Property(entity => entity.SourceSystem).HasColumnName("source_system").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.SourceRecordId).HasColumnName("source_record_id").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.LastSynchronizedAtUtc).HasColumnName("last_synchronized_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.StartsAtUtc, entity.EndsAtUtc });
        builder.HasOne<Practitioner>().WithMany().HasForeignKey(entity => new { entity.PractitionerId, entity.OrganizationId }).HasPrincipalKey(practitioner => new { practitioner.Id, practitioner.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Site>().WithMany().HasForeignKey(entity => new { entity.SiteId, entity.OrganizationId }).HasPrincipalKey(site => new { site.Id, site.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(entity => new { entity.DepartmentId, entity.OrganizationId }).HasPrincipalKey(department => new { department.Id, department.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DirectorySourceRecordConfiguration : IEntityTypeConfiguration<DirectorySourceRecord>
{
    public void Configure(EntityTypeBuilder<DirectorySourceRecord> builder)
    {
        builder.ToTable("directory_source_records");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new DirectorySourceRecordId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.PractitionerId).HasColumnName("practitioner_id").HasConversion(
            id => id.HasValue ? id.Value.Value : (Guid?)null,
            value => value.HasValue ? new PractitionerId(value.Value) : null);
        builder.Property(entity => entity.SourceSystem).HasColumnName("source_system").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.SourceRecordId).HasColumnName("source_record_id").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.SourceUpdatedAtUtc).HasColumnName("source_updated_at_utc").IsRequired();
        builder.Property(entity => entity.PayloadHash).HasColumnName("payload_hash").HasMaxLength(128).IsRequired();
        builder.Property(entity => entity.LastSeenAtUtc).HasColumnName("last_seen_at_utc").IsRequired();
        builder.Property(entity => entity.SyncState).HasColumnName("sync_state").HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.IsStale).HasColumnName("is_stale").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.SourceSystem, entity.SourceRecordId }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DirectorySyncRunConfiguration : IEntityTypeConfiguration<DirectorySyncRun>
{
    public void Configure(EntityTypeBuilder<DirectorySyncRun> builder)
    {
        builder.ToTable("directory_sync_runs");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new DirectorySyncRunId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.SourceSystem).HasColumnName("source_system").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.StartedAtUtc).HasColumnName("started_at_utc").IsRequired();
        builder.Property(entity => entity.EndedAtUtc).HasColumnName("ended_at_utc");
        builder.Property(entity => entity.InsertedCount).HasColumnName("inserted_count").IsRequired();
        builder.Property(entity => entity.UpdatedCount).HasColumnName("updated_count").IsRequired();
        builder.Property(entity => entity.DeactivatedCount).HasColumnName("deactivated_count").IsRequired();
        builder.Property(entity => entity.RejectedCount).HasColumnName("rejected_count").IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(entity => entity.CorrelationId).HasColumnName("correlation_id").HasMaxLength(96).IsRequired();
        builder.Property(entity => entity.ErrorSummary).HasColumnName("error_summary").HasMaxLength(500).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.StartedAtUtc });
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
