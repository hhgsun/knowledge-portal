using System.Security.Cryptography;

namespace KnowledgePortal.Api.Auth;

/// <summary>
/// Generates API keys in the "kp_" + 32 hex chars format.
/// The raw key is only shown once; only its BCrypt hash and lookup prefix are stored.
/// </summary>
public static class ApiKeyGenerator
{
    public record GeneratedKey(string RawKey, string Hash, string Prefix);

    public static GeneratedKey Generate()
    {
        var rawKey = "kp_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        return new GeneratedKey(
            rawKey,
            BCrypt.Net.BCrypt.HashPassword(rawKey, 12),
            rawKey[3..11]); // First 8 chars after "kp_" for indexed lookup
    }
}
