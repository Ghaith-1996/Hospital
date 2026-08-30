using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Domain.Reliability;
using Microsoft.EntityFrameworkCore;

namespace CriticalAlerts.Infrastructure.Persistence;

public sealed class CriticalAlertsDbContext : DbContext
{
    public CriticalAlertsDbContext(DbContextOptions<CriticalAlertsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<UserAccount> Users => Set<UserAccount>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();

    public DbSet<Practitioner> Practitioners => Set<Practitioner>();

    public DbSet<PractitionerRoleAssignment> PractitionerRoles => Set<PractitionerRoleAssignment>();

    public DbSet<ContactEndpoint> ContactEndpoints => Set<ContactEndpoint>();

    public DbSet<OnCallAssignment> OnCallAssignments => Set<OnCallAssignment>();

    public DbSet<DirectorySourceRecord> DirectorySourceRecords => Set<DirectorySourceRecord>();

    public DbSet<DirectorySyncRun> DirectorySyncRuns => Set<DirectorySyncRun>();

    public DbSet<AlertTemplate> AlertTemplates => Set<AlertTemplate>();

    public DbSet<NotificationPolicy> NotificationPolicies => Set<NotificationPolicy>();

    public DbSet<EscalationPolicy> EscalationPolicies => Set<EscalationPolicy>();

    public DbSet<EscalationStep> EscalationSteps => Set<EscalationStep>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<AlertFieldConfirmation> AlertFieldConfirmations => Set<AlertFieldConfirmation>();

    public DbSet<AlertRecipientSelection> AlertRecipientSelections => Set<AlertRecipientSelection>();

    public DbSet<AlertStateTransition> AlertStateTransitions => Set<AlertStateTransition>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<DeliveryEvent> DeliveryEvents => Set<DeliveryEvent>();

    public DbSet<SimulationDispatchScenarioSetting> SimulationDispatchScenarioSettings => Set<SimulationDispatchScenarioSetting>();

    public DbSet<RecipientResponse> RecipientResponses => Set<RecipientResponse>();

    public DbSet<ResponsibilityAssignment> ResponsibilityAssignments => Set<ResponsibilityAssignment>();

    public DbSet<EscalationRun> EscalationRuns => Set<EscalationRun>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CriticalAlertsDbContext).Assembly);
    }
}
