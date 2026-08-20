using System.Security.Cryptography;
using System.Text;
using CriticalAlerts.Application.Protection;
using CriticalAlerts.Domain;

namespace CriticalAlerts.Infrastructure.Protection;

public sealed class AesGcmSensitiveDataProtector : ISensitiveDataProtector
{
    public const string LocalKeyVersion = "local-v1";

    private readonly byte[] key;

    public AesGcmSensitiveDataProtector(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new InvalidOperationException("The local sensitive-data key must be 32 bytes.");
        }

        this.key = key;
    }

    public static AesGcmSensitiveDataProtector FromBase64(string? keyMaterial)
    {
        if (string.IsNullOrWhiteSpace(keyMaterial))
        {
            throw new InvalidOperationException("CRITICAL_ALERTS_DATA_PROTECTION_KEY is required in Development/Test.");
        }

        return new AesGcmSensitiveDataProtector(Convert.FromBase64String(keyMaterial));
    }

    public ProtectedValue Protect(string plaintext, SensitiveDataContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        var packed = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, packed, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, packed, nonce.Length + tag.Length, ciphertext.Length);
        return new ProtectedValue(packed, LocalKeyVersion, context.Purpose);
    }

    public string Unprotect(ProtectedValue protectedValue, SensitiveDataContext context)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        if (protectedValue.Purpose != context.Purpose)
        {
            throw new InvalidOperationException("Protected value purpose does not match the requested context.");
        }

        var packed = protectedValue.Ciphertext;
        var nonce = packed.AsSpan(0, 12);
        var tag = packed.AsSpan(12, 16);
        var ciphertext = packed.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }
}
