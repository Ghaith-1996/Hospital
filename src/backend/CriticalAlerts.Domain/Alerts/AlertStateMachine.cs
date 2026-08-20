namespace CriticalAlerts.Domain.Alerts;

public static class AlertStateMachine
{
    private static readonly HashSet<(AlertState From, AlertState To)> Allowed =
    [
        (AlertState.Draft, AlertState.PendingConfirmation),
        (AlertState.PendingConfirmation, AlertState.Draft),
        (AlertState.PendingConfirmation, AlertState.DispatchQueued),
        (AlertState.DispatchQueued, AlertState.Active),
        (AlertState.DispatchQueued, AlertState.Failed),
        (AlertState.Active, AlertState.Active),
        (AlertState.Active, AlertState.Failed),
        (AlertState.Active, AlertState.Resolved),
        (AlertState.Draft, AlertState.Cancelled),
        (AlertState.PendingConfirmation, AlertState.Cancelled),
        (AlertState.Active, AlertState.Cancelled),
        (AlertState.Failed, AlertState.Active),
    ];

    public static bool CanTransition(AlertState from, AlertState to)
    {
        if (from == to && from == AlertState.Draft)
        {
            return true;
        }

        return Allowed.Contains((from, to));
    }

    public static IReadOnlyCollection<(AlertState From, AlertState To)> AllowedTransitions => Allowed.ToArray();
}
