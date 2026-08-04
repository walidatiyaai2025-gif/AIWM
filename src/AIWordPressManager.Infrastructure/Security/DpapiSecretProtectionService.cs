using System.Security.Cryptography;
using System.Text;
using AIWordPressManager.Application.Abstractions;

namespace AIWordPressManager.Infrastructure.Security;

public sealed class DpapiSecretProtectionService : ISecretProtectionService
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIWordPressManager:v1");

    public Task<string> ProtectAsync(string plainText, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plainText);
        cancellationToken.ThrowIfCancellationRequested();
        var clearBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(clearBytes, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(clearBytes);
        return Task.FromResult(Convert.ToBase64String(protectedBytes));
    }

    public Task<string> UnprotectAsync(string protectedValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedValue);
        cancellationToken.ThrowIfCancellationRequested();
        var protectedBytes = Convert.FromBase64String(protectedValue);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Task.FromResult(Encoding.UTF8.GetString(clearBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }
}
