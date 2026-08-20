using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("users");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new UserId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.SimulationHandle).HasColumnName("simulation_handle").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(entity => entity.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.SimulationHandle }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new RoleId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.HasAlternateKey(entity => new { entity.Id, entity.OrganizationId });
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(entity => new { entity.OrganizationId, entity.UserId, entity.RoleId });
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.UserId).GuidId(value => new UserId(value), id => id.Value, "user_id");
        builder.Property(entity => entity.RoleId).GuidId(value => new RoleId(value), id => id.Value, "role_id");
        builder.HasIndex(entity => new { entity.OrganizationId, entity.UserId, entity.RoleId })
            .IsUnique()
            .HasDatabaseName("UX_user_roles_organization_id_user_id_role_id");
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => new { entity.UserId, entity.OrganizationId }).HasPrincipalKey(user => new { user.Id, user.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(entity => new { entity.RoleId, entity.OrganizationId }).HasPrincipalKey(role => new { role.Id, role.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExternalIdentityConfiguration : IEntityTypeConfiguration<ExternalIdentity>
{
    public void Configure(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.ToTable("external_identities");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(value => new ExternalIdentityId(value), id => id.Value, "id");
        builder.Property(entity => entity.OrganizationId).GuidId(value => new OrganizationId(value), id => id.Value, "organization_id");
        builder.Property(entity => entity.UserId).GuidId(value => new UserId(value), id => id.Value, "user_id");
        builder.Property(entity => entity.Provider).HasColumnName("provider").HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Provider, entity.Subject }).IsUnique();
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(entity => new { entity.UserId, entity.OrganizationId }).HasPrincipalKey(user => new { user.Id, user.OrganizationId }).OnDelete(DeleteBehavior.Restrict);
    }
}
