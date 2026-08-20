namespace CriticalAlerts.Domain;

/// <summary>Ciphertext plus key version for a sensitive field. Plaintext never belongs in logs or outbox payloads.</summary>
public sealed class ProtectedValue
{
    private ProtectedValue()
    {
        Ciphertext = [];
        KeyVersion = string.Empty;
        Purpose = string.Empty;
    }

    public ProtectedValue(byte[] ciphertext, string keyVersion, string purpose)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (ciphertext.Length == 0)
        {
            throw new DomainException("Protected values cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(keyVersion))
        {
            throw new DomainException("Protected values require a key version.");
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new DomainException("Protected values require a purpose.");
        }

        Ciphertext = ciphertext;
        KeyVersion = keyVersion;
        Purpose = purpose;
    }

    public byte[] Ciphertext { get; private set; }

    public string KeyVersion { get; private set; }

    public string Purpose { get; private set; }
}
