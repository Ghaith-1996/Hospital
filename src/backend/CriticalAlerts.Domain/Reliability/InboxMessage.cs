namespace CriticalAlerts.Domain.Reliability;

public sealed class InboxMessage
{
    private InboxMessage()
    {
        ExternalMessageId = string.Empty;
        Handler = string.Empty;
        Result = string.Empty;
    }

    private InboxMessage(
        InboxMessageId id,
        OrganizationId organizationId,
        string externalMessageId,
        string handler,
        string result,
        DateTimeOffset processedAtUtc)
    {
        Id = id;
        OrganizationId = organizationId;
        ExternalMessageId = externalMessageId;
        Handler = handler;
        Result = result;
        ProcessedAtUtc = processedAtUtc;
    }

    public InboxMessageId Id { get; private set; }

    public OrganizationId OrganizationId { get; private set; }

    public string ExternalMessageId { get; private set; }

    public string Handler { get; private set; }

    public string Result { get; private set; }

    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static InboxMessage Create(
        InboxMessageId id,
        OrganizationId organizationId,
        string externalMessageId,
        string handler,
        string result,
        DateTimeOffset processedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(externalMessageId) || string.IsNullOrWhiteSpace(handler))
        {
            throw new DomainException("Inbox messages require an external message ID and handler.");
        }

        return new InboxMessage(
            id,
            organizationId,
            externalMessageId.Trim(),
            handler.Trim(),
            result.Trim(),
            UtcInstant.Require(processedAtUtc, nameof(processedAtUtc)));
    }
}
