using CriticalAlerts.Domain;
using CriticalAlerts.Domain.Directory;

namespace CriticalAlerts.Infrastructure.Directory;

internal static class ContactEndpointChannels
{
    public static NotificationChannel ToNotificationChannel(ContactEndpointKind kind)
        => kind switch
        {
            ContactEndpointKind.SecureMessage => NotificationChannel.SecureMessage,
            ContactEndpointKind.Sms => NotificationChannel.Sms,
            ContactEndpointKind.Voice => NotificationChannel.Voice,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported contact endpoint kind."),
        };
}
