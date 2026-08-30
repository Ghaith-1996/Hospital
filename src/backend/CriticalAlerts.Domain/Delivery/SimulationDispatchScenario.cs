namespace CriticalAlerts.Domain.Delivery;

public enum SimulationDispatchScenario
{
    ImmediateSuccess = 0,
    DelayedDelivery = 1,
    SmsFailure = 2,
    VoiceNoAnswer = 3,
    ProviderOutage = 4,
    DuplicateCallback = 5,
    OutOfOrderCallback = 6,
}
