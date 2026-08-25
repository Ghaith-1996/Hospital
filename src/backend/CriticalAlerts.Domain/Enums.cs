namespace CriticalAlerts.Domain;

/// <summary>
/// Alert lifecycle states from the Phase 0 architecture. Delivery, opened, acknowledged,
/// and responsibility accepted are recipient/channel dimensions, not alert lifecycle states.
/// Names such as AwaitingConfirmation, Approved, Dispatching, Dispatched, Delivered, and
/// Accepted from later summaries map onto these states or onto delivery/response records.
/// </summary>
public enum AlertState
{
    Draft = 0,
    PendingConfirmation = 1,
    DispatchQueued = 2,
    Active = 3,
    Resolved = 4,
    Cancelled = 5,
    Failed = 6,
}

public enum NotificationChannel
{
    SecureMessage = 0,
    Sms = 1,
    Voice = 2,
}

public enum FieldConfirmationStatus
{
    Unresolved = 0,
    Confirmed = 1,
}

public enum AlertSourceType
{
    Typed = 0,
    Dictated = 1,
}

public enum ObservationState
{
    PendingNotObserved = 0,
    Occurred = 1,
    Failed = 2,
    NotApplicable = 3,
}

public enum DeliveryAttemptStatus
{
    Requested = 0,
    Submitted = 1,
    Delivered = 2,
    Failed = 3,
}

public enum RecipientResponseType
{
    Acknowledged = 0,
    Accepted = 1,
    Declined = 2,
    Unavailable = 3,
    CallUnitRequested = 4,
}

public enum OnCallTier
{
    Primary = 0,
    Backup = 1,
}

public enum ContactEndpointKind
{
    SecureMessage = 0,
    Sms = 1,
    Voice = 2,
}

public enum OutboxProcessingState
{
    Pending = 0,
    Processing = 1,
    Processed = 2,
    Failed = 3,
}

public enum IdempotencyProcessingStatus
{
    Started = 0,
    Completed = 1,
    Failed = 2,
}

public enum DirectorySyncRunStatus
{
    Succeeded = 0,
    Failed = 1,
    Partial = 2,
    InProgress = 3,
}

public enum EscalationRunState
{
    Scheduled = 0,
    Running = 1,
    Completed = 2,
    Stopped = 3,
}
