using System.Security.Cryptography;
using System.Text;
using Fnb.Application.Abstractions;
using Fnb.Application.Common;
using Fnb.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace Fnb.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's PBKDF2 password hasher (V3 format, 100k+ iterations).</summary>
public sealed class IdentityPasswordService : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new();
    private readonly Lazy<string> _dummyHash;

    public IdentityPasswordService()
    {
        _dummyHash = new Lazy<string>(() => _hasher.HashPassword(DummyUser, SecureTokens.Create()));
    }

    private static readonly User DummyUser = new() { UserName = "dummy", NormalizedUserName = "DUMMY", DisplayName = "dummy" };

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return false;
        }

        try
        {
            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
            return result != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void VerifyDummy(string password) => _hasher.VerifyHashedPassword(DummyUser, _dummyHash.Value, password);
}

public sealed class HmacTrackingTokenService : ITrackingTokenService
{
    private readonly byte[] _key;

    public HmacTrackingTokenService(IConfiguration configuration)
    {
        var secret = configuration["Ordering:TrackingTokenSecret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException("Configuration 'Ordering:TrackingTokenSecret' is required (>= 32 characters).");
        }

        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Create(Guid orderId)
    {
        var mac = HMACSHA256.HashData(_key, orderId.ToByteArray());
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public string Hash(string token) => SecureTokens.Sha256(token);
}
