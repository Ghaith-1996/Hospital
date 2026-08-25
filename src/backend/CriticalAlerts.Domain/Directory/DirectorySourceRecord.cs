namespace CriticalAlerts.Domain.Directory;

public sealed class DirectorySourceRecord
{
    private DirectorySourceRecord()
    {
        SourceSystem = string.Empty;
        SourceRecordId = string.Empty;
        PayloadHash = string.Empty;
        SyncState = string.Empty;
    }

    private DirectorySourceRecord(
        DirectorySourceRecordId id,
        OrganizationId organizationId,
        PractitionerId? practitionerId,
        string sourceSystem,
        string sourceRecordId,
        DateTimeOffset sourceUpdatedAtUtc,
        string payloadHash,
        DateTimeOffset lastSeenAtUtc,
        string syncState,
        bool isStale)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        SourceSystem = sourceSystem;
        SourceRecordId = sourceRecordId;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        PayloadHash = payloadHash;
        LastSeenAtUtc = lastSeenAtUtc;
        SyncState = syncState;
        IsStale = isStale;
    }

    public DirectorySourceRecordId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId? PractitionerId { get; private set; }

    public string SourceSystem { get; private set; }

    public string SourceRecordId { get; private set; }

    public DateTimeOffset SourceUpdatedAtUtc { get; private set; }

    public string PayloadHash { get; private set; }

    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public string SyncState { get; private set; }

    public bool IsStale { get; private set; }

    public static DirectorySourceRecord Create(
        DirectorySourceRecordId id,
        OrganizationId organizationId,
        PractitionerId? practitionerId,
        string sourceSystem,
        string sourceRecordId,
        DateTimeOffset sourceUpdatedAtUtc,
        string payloadHash,
        DateTimeOffset lastSeenAtUtc,
        string syncState,
        bool isStale)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(sourceRecordId))
        {
            throw new DomainException("Directory source records require a source system and identifier.");
        }

        return new DirectorySourceRecord(
            id,
            organizationId,
            practitionerId,
            sourceSystem.Trim(),
            sourceRecordId.Trim(),
            UtcInstant.Require(sourceUpdatedAtUtc, nameof(sourceUpdatedAtUtc)),
            payloadHash.Trim(),
            UtcInstant.Require(lastSeenAtUtc, nameof(lastSeenAtUtc)),
            syncState.Trim(),
            isStale);
    }

    public void Refresh(PractitionerId practitionerId, DateTimeOffset sourceUpdatedAtUtc, string payloadHash, DateTimeOffset lastSeenAtUtc, bool isStale)
    {
        PractitionerId = practitionerId;
        SourceUpdatedAtUtc = UtcInstant.Require(sourceUpdatedAtUtc, nameof(sourceUpdatedAtUtc));
        PayloadHash = payloadHash.Trim();
        LastSeenAtUtc = UtcInstant.Require(lastSeenAtUtc, nameof(lastSeenAtUtc));
        SyncState = isStale ? "stale" : "current";
        IsStale = isStale;
    }
}
