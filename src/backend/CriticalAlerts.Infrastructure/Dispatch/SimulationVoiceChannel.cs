using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Dispatch;

public sealed class SimulationVoiceChannel(TimeProvider time) : SimulationChannelBase(time)
{
    public override NotificationChannel ChannelType => NotificationChannel.Voice;

    public override string ProviderName => "simulation-voice";
}
