using System.Security.Cryptography;

namespace PingDashboard.Services;

/// <summary>
/// PBKDF2 (SHA-256) password hashing — no external dependencies.
/// Produces a random per-user salt and a derived hash, both Base64-encoded.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize   = 16;      // 128-bit salt
    private const int KeySize    = 32;      // 256-bit hash
    private const int Iterations = 100_000;

    public static (string hash, string salt) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key  = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string storedHash, string storedSalt)
    {
        try
        {
            byte[] salt   = Convert.FromBase64String(storedSalt);
            byte[] stored = Convert.FromBase64String(storedHash);
            byte[] key    = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return CryptographicOperations.FixedTimeEquals(key, stored);
        }
        catch
        {
            return false;
        }
    }
}
