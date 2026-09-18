using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;

namespace VisionMaven.Infrastructure.Security;

/// <summary>DPAPI 加密（绑定当前 Windows 用户），用于 MES Token / 数据库口令等敏感串。</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VisionMaven.Secret.v1");

    private readonly ILogger<DpapiSecretProtector> _logger;

    public DpapiSecretProtector(ILogger<DpapiSecretProtector> logger)
    {
        _logger = logger;
    }

    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText) || IsProtected(plainText))
        {
            return plainText;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var cipher = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(cipher);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "当前平台不支持 DPAPI，敏感串以明文保存");
            return plainText;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "敏感串加密失败，以明文保存");
            return plainText;
        }
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText) || !IsProtected(protectedText))
        {
            return protectedText;
        }

        try
        {
            var cipher = Convert.FromBase64String(protectedText[Prefix.Length..]);
            var bytes = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or PlatformNotSupportedException)
        {
            _logger.LogWarning(ex, "敏感串解密失败，按原文返回");
            return protectedText;
        }
    }

    public static bool IsProtected(string value)
        => value.StartsWith(Prefix, StringComparison.Ordinal);
}
