using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CriticalAlerts.Infrastructure.Persistence.Configurations;

internal sealed class SimulationDispatchScenarioConfiguration : IEntityTypeConfiguration<SimulationDispatchScenarioSetting>
{
    public void Configure(EntityTypeBuilder<SimulationDispatchScenarioSetting> builder)
    {
        builder.ToTable("simulation_dispatch_scenarios");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).GuidId(
            value => new SimulationDispatchScenarioSettingId(value),
            id => id.Value,
            "id");
        builder.Property(entity => entity.OrganizationId).GuidId(
            value => new OrganizationId(value),
            id => id.Value,
            "organization_id");
        builder.Property(entity => entity.Channel)
            .HasColumnName("channel")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(entity => entity.Scenario)
            .HasColumnName("scenario")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();
        builder.Property(entity => entity.UpdatedByUserId).GuidId(
            value => new UserId(value),
            id => id.Value,
            "updated_by_user_id");
        builder.Property(entity => entity.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.HasIndex(entity => new { entity.OrganizationId, entity.Channel }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(entity => entity.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UserAccount>()
            .WithMany()
            .HasForeignKey(entity => new { entity.UpdatedByUserId, entity.OrganizationId })
            .HasPrincipalKey(user => new { user.Id, user.OrganizationId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
