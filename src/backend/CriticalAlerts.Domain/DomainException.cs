namespace CriticalAlerts.Domain;

/// <summary>A domain rule violation that must be rejected by persistence and API layers.</summary>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidAlertTransitionException : DomainException
{
    public InvalidAlertTransitionException(string message)
        : base(message)
    {
    }
}

public sealed class StaleAlertVersionException : DomainException
{
    public StaleAlertVersionException(string message)
        : base(message)
    {
    }
}

public sealed class OrganizationIsolationException : DomainException
{
    public OrganizationIsolationException(string message)
        : base(message)
    {
    }
}

public sealed class InactivePractitionerException : DomainException
{
    public InactivePractitionerException(string message)
        : base(message)
    {
    }
}

public sealed class DuplicateRecipientException : DomainException
{
    public DuplicateRecipientException(string message)
        : base(message)
    {
    }
}

public sealed class RecipientsRequiredException : DomainException
{
    public RecipientsRequiredException(string message)
        : base(message)
    {
    }
}

public sealed class UnresolvedCriticalFieldException : DomainException
{
    public UnresolvedCriticalFieldException(string message)
        : base(message)
    {
    }
}

public sealed class NonUtcTimestampException : DomainException
{
    public NonUtcTimestampException(string message)
        : base(message)
    {
    }
}
