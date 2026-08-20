using CriticalAlerts.Domain;

namespace CriticalAlerts.Application.Protection;

public sealed record SensitiveDataContext(string Purpose, Guid OrganizationId);

public interface ISensitiveDataProtector
{
    ProtectedValue Protect(string plaintext, SensitiveDataContext context);

    string Unprotect(ProtectedValue protectedValue, SensitiveDataContext context);
}
