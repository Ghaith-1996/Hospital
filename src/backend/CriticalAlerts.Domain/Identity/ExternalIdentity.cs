namespace CriticalAlerts.Domain.Identity;

public sealed class ExternalIdentity
{
    private ExternalIdentity()
    {
        Provider = string.Empty;
        Subject = string.Empty;
    }

    private ExternalIdentity(
        ExternalIdentityId id,
        OrganizationId organizationId,
        UserId userId,
        string provider,
        string subject)
    {
        Id = id;
        OrganizationId = organizationId;
        UserId = userId;
        Provider = provider;
        Subject = subject;
    }

    public ExternalIdentityId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public UserId UserId { get; private set; }

    public string Provider { get; private set; }

    public string Subject { get; private set; }

    public static ExternalIdentity Create(
        ExternalIdentityId id,
        OrganizationId organizationId,
        UserId userId,
        string provider,
        string subject)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainException("External identities require a provider and subject.");
        }

        return new ExternalIdentity(id, organizationId, userId, provider.Trim(), subject.Trim());
    }
}
