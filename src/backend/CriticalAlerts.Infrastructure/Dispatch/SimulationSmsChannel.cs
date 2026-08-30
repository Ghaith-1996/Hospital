using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class SimulationSmsChannel(TimeProvider time) : SimulationChannelBase(time)
{
    public override NotificationChannel ChannelType => NotificationChannel.Sms;

    public override string ProviderName => "simulation-sms";
}
