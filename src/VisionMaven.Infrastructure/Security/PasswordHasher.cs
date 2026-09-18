using System.Security.Cryptography;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Infrastructure.Security;

/// <summary>PBKDF2-HMAC-SHA256：迭代 100000 次，16 字节随机盐，32 字节哈希。</summary>
public sealed class PasswordHasher : IPasswordHasher
{
    public const int DefaultIterations = 100_000;

    private const int SaltSize = 16;
    private const int HashSize = 32;

    public (string Hash, string Salt, int Iterations) Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA256,
            HashSize);

        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
    }

    public bool Verify(string password, string hash, string salt, int iterations)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                iterations <= 0 ? DefaultIterations : iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>密码强度校验：最少 8 位，同时含字母与数字。</summary>
    public static bool IsStrong(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return false;
        }

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        return hasLetter && hasDigit;
    }
}
