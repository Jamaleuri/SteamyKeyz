using System.Security.Cryptography;

namespace SteamyKeyz.Services;

/// <summary>
/// BCrypt-based password hashing. No external NuGet package needed —
/// uses PBKDF2 (RFC 2898) from the .NET crypto primitives.
/// Format: {iterations}.{base64-salt}.{base64-hash}
/// </summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;      // 128 bit
    private const int KeySize = 32;       // 256 bit
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string passwordHash)
    {
        var parts = passwordHash.Split('.', 3);
        if (parts.Length != 3) return false;

        var iterations = int.Parse(parts[0]);
        var salt = Convert.FromBase64String(parts[1]);
        var hash = Convert.FromBase64String(parts[2]);

        var testHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, hash.Length);

        return CryptographicOperations.FixedTimeEquals(hash, testHash);
    }
}
