using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Dispatch;

public sealed class DispatchValidationException(string code, string message) : DomainException(message)
{
    public string Code { get; } = code;
}
