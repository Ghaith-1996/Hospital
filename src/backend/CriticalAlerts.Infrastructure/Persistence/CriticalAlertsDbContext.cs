using CriticalAlerts.Domain.Alerts;
using CriticalAlerts.Domain.Delivery;
using CriticalAlerts.Domain.Directory;
using CriticalAlerts.Domain.Identity;
using CriticalAlerts.Domain.Organizations;
using CriticalAlerts.Domain.Policies;
using CriticalAlerts.Domain.Reliability;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public DbSet<PractitionerUserLink> PractitionerUserLinks => Set<PractitionerUserLink>();

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

    public DbSet<AlertSourceRevision> AlertSourceRevisions => Set<AlertSourceRevision>();

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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateException exception) when (IsSourceRevisionConflict(exception))
        {
            throw new DbUpdateConcurrencyException("The alert draft version has changed. Reload the alert before editing.", exception);
        }
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException exception) when (IsSourceRevisionConflict(exception))
        {
            throw new DbUpdateConcurrencyException("The alert draft version has changed. Reload the alert before editing.", exception);
        }
    }

    // EF may insert the revision before checking the parent alert's xmin concurrency token.
    // A collision on this exact version index is the same stale draft conflict; base.SaveChanges
    // has already rolled back the transaction or savepoint before the exception reaches here.
    private static bool IsSourceRevisionConflict(DbUpdateException exception)
        => exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UX_alert_source_revisions_alert_id_alert_version",
        };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CriticalAlertsDbContext).Assembly);
    }
}
