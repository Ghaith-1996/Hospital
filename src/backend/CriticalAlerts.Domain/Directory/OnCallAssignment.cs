namespace CriticalAlerts.Domain.Directory;

public sealed class OnCallAssignment
{
    private OnCallAssignment()
    {
        SourceSystem = string.Empty;
        SourceRecordId = string.Empty;
    }

    private OnCallAssignment(
        OnCallAssignmentId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        SiteId siteId,
        DepartmentId departmentId,
        OnCallTier tier,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string sourceSystem,
        string sourceRecordId,
        DateTimeOffset lastSynchronizedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        PractitionerId = practitionerId;
        SiteId = siteId;
        DepartmentId = departmentId;
        Tier = tier;
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
        SourceSystem = sourceSystem;
        SourceRecordId = sourceRecordId;
        LastSynchronizedAtUtc = lastSynchronizedAtUtc;
    }

    public OnCallAssignmentId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public PractitionerId PractitionerId { get; private set; }

    public SiteId SiteId { get; private set; }

    public DepartmentId DepartmentId { get; private set; }

    public OnCallTier Tier { get; private set; }

    public DateTimeOffset StartsAtUtc { get; private set; }

    public DateTimeOffset EndsAtUtc { get; private set; }

    public string SourceSystem { get; private set; }

    public string SourceRecordId { get; private set; }

    public DateTimeOffset LastSynchronizedAtUtc { get; private set; }

    public static OnCallAssignment Create(
        OnCallAssignmentId id,
        OrganizationId organizationId,
        PractitionerId practitionerId,
        SiteId siteId,
        DepartmentId departmentId,
        OnCallTier tier,
        DateTimeOffset startsAtUtc,
        DateTimeOffset endsAtUtc,
        string sourceSystem,
        string sourceRecordId,
        DateTimeOffset lastSynchronizedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(sourceSystem) || string.IsNullOrWhiteSpace(sourceRecordId))
        {
            throw new DomainException("On-call assignments require a source system and source record.");
        }

        var normalizedStartsAtUtc = UtcInstant.Require(startsAtUtc, nameof(startsAtUtc));
        var normalizedEndsAtUtc = UtcInstant.Require(endsAtUtc, nameof(endsAtUtc));
        if (normalizedEndsAtUtc <= normalizedStartsAtUtc)
        {
            throw new DomainException("On-call assignments require an end timestamp after the start timestamp.");
        }

        return new OnCallAssignment(
            id,
            organizationId,
            practitionerId,
            siteId,
            departmentId,
            tier,
            normalizedStartsAtUtc,
            normalizedEndsAtUtc,
            sourceSystem.Trim(),
            sourceRecordId.Trim(),
            UtcInstant.Require(lastSynchronizedAtUtc, nameof(lastSynchronizedAtUtc)));
    }
}
