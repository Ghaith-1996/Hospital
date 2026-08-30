using CriticalAlerts.Domain.Delivery;

namespace CriticalAlerts.Infrastructure.Dispatch;

public static class SimulationProviderScenarioCatalog
{
    public static IReadOnlyList<SimulationDispatchScenario> Supported { get; } =
    [
        SimulationDispatchScenario.ImmediateSuccess,
        SimulationDispatchScenario.DelayedDelivery,
        SimulationDispatchScenario.SmsFailure,
        SimulationDispatchScenario.VoiceNoAnswer,
        SimulationDispatchScenario.ProviderOutage,
        SimulationDispatchScenario.DuplicateCallback,
        SimulationDispatchScenario.OutOfOrderCallback,
    ];

    public static bool TryParse(string? value, out SimulationDispatchScenario scenario)
    {
        if (value is not null
            && Enum.TryParse(value.Trim(), ignoreCase: false, out scenario)
            && Supported.Contains(scenario))
        {
            return true;
        }

        scenario = default;
        return false;
    }
}
