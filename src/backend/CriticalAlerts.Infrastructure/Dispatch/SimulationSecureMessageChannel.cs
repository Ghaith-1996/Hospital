using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class SimulationSecureMessageChannel(TimeProvider time) : SimulationChannelBase(time)
{
    public override NotificationChannel ChannelType => NotificationChannel.SecureMessage;

    public override string ProviderName => "simulation-secure-message";
}
