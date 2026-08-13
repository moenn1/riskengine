using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TradingRisk.Api.Options;

namespace TradingRisk.Api.Security;

public sealed record AuthenticatedUser(string UserName, string Role);

/// <summary>
/// Local credential verifier for learning. It demonstrates salted PBKDF2, generic
/// failures, and lockout. Production should delegate this boundary to an OIDC provider.
/// </summary>
public sealed class CredentialValidator(IOptions<SecurityOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, FailureState> failures = new(StringComparer.OrdinalIgnoreCase);

    public bool TryValidate(string? userName, string? password, out AuthenticatedUser? user)
    {
        user = null;
        var normalized = userName?.Trim() ?? "";
        var configured = options.Value.DemoUsers.FirstOrDefault(
            candidate => string.Equals(candidate.UserName, normalized, StringComparison.OrdinalIgnoreCase));
        var state = failures.GetOrAdd(normalized, _ => new FailureState());
        var now = clock.GetUtcNow();
        if (state.LockedUntilUtc > now)
        {
            return false;
        }

        // Use the configured fake hash for unknown users too, reducing username timing leaks.
        var hash = configured?.PasswordHash ??
            (options.Value.DemoUsers.Count > 0 ? options.Value.DemoUsers[0].PasswordHash : null);
        var valid = hash is not null && !string.IsNullOrEmpty(password) && Verify(password, hash);
        if (!valid)
        {
            var failed = Interlocked.Increment(ref state.FailedAttempts);
            if (failed >= options.Value.MaxFailedAttempts)
            {
                state.LockedUntilUtc = now.AddMinutes(options.Value.LockoutMinutes);
                Interlocked.Exchange(ref state.FailedAttempts, 0);
            }
            return false;
        }

        failures.TryRemove(normalized, out _);
        user = new AuthenticatedUser(configured!.UserName, configured.Role);
        return true;
    }

    private static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" ||
            !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, iterations,
                HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private sealed class FailureState
    {
        public int FailedAttempts;
        public DateTimeOffset LockedUntilUtc;
    }
}
