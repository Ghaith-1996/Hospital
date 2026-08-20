namespace CriticalAlerts.Domain;

internal static class UtcInstant
{
    public static DateTimeOffset Require(DateTimeOffset value, string name)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new NonUtcTimestampException($"{name} must be stored as UTC.");
        }

        return value;
    }
}
