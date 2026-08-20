namespace CriticalAlerts.Domain.Directory;

public sealed class DirectorySyncRun
{
    private DirectorySyncRun()
    {
        SourceSystem = string.Empty;
        CorrelationId = string.Empty;
        ErrorSummary = string.Empty;
    }

    private DirectorySyncRun(
        DirectorySyncRunId id,
        OrganizationId organizationId,
        string sourceSystem,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? endedAtUtc,
        int insertedCount,
        int updatedCount,
        int deactivatedCount,
        int rejectedCount,
        DirectorySyncRunStatus status,
        string correlationId,
        string errorSummary)
    {
        Id = id;
        OrganizationId = organizationId;
        SourceSystem = sourceSystem;
        StartedAtUtc = startedAtUtc;
        EndedAtUtc = endedAtUtc;
        InsertedCount = insertedCount;
        UpdatedCount = updatedCount;
        DeactivatedCount = deactivatedCount;
        RejectedCount = rejectedCount;
        Status = status;
        CorrelationId = correlationId;
        ErrorSummary = errorSummary;
    }

    public DirectorySyncRunId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string SourceSystem { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public int InsertedCount { get; private set; }

    public int UpdatedCount { get; private set; }

    public int DeactivatedCount { get; private set; }

    public int RejectedCount { get; private set; }

    public DirectorySyncRunStatus Status { get; private set; }

    public string CorrelationId { get; private set; }

    public string ErrorSummary { get; private set; }

    public static DirectorySyncRun CreateCompleted(
        DirectorySyncRunId id,
        OrganizationId organizationId,
        string sourceSystem,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        int insertedCount,
        int updatedCount,
        int deactivatedCount,
        int rejectedCount,
        DirectorySyncRunStatus status,
        string correlationId,
        string errorSummary)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(correlationId))
        {
            throw new DomainException("Directory sync runs require a source and correlation ID.");
        }

        return new DirectorySyncRun(
            id,
            organizationId,
            sourceSystem.Trim(),
            UtcInstant.Require(startedAtUtc, nameof(startedAtUtc)),
            UtcInstant.Require(endedAtUtc, nameof(endedAtUtc)),
            insertedCount,
            updatedCount,
            deactivatedCount,
            rejectedCount,
            status,
            correlationId.Trim(),
            errorSummary.Trim());
    }
}
