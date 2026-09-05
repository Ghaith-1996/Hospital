namespace CriticalAlerts.Domain.Alerts;

/// <summary>
/// Immutable source content for one alert draft version. A later source edit creates another
/// row; it never rewrites the first source or any previously persisted revision.
/// </summary>
public sealed class AlertSourceRevision
{
    private AlertSourceRevision()
    {
        Source = null!;
    }

    private AlertSourceRevision(
        AlertSourceRevisionId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        AlertSourceType sourceType,
        ProtectedValue source,
        UserId createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        AlertId = alertId;
        AlertVersion = alertVersion;
        SourceType = sourceType;
        // Every revision owns its protected value, even when the source text is unchanged.
        Source = new ProtectedValue(source.Ciphertext.ToArray(), source.KeyVersion, source.Purpose);
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = UtcInstant.Require(createdAtUtc, nameof(createdAtUtc));
    }

    public AlertSourceRevisionId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public AlertId AlertId { get; private set; }

    public AlertDraftVersion AlertVersion { get; private set; }

    public AlertSourceType SourceType { get; private set; }

    public ProtectedValue Source { get; private set; }

    public UserId CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AlertSourceRevision Create(
        AlertSourceRevisionId id,
        OrganizationId organizationId,
        AlertId alertId,
        AlertDraftVersion alertVersion,
        AlertSourceType sourceType,
        ProtectedValue source,
        UserId createdByUserId,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new AlertSourceRevision(
            id,
            organizationId,
            alertId,
            alertVersion,
            sourceType,
            source,
            createdByUserId,
            createdAtUtc);
    }
}
