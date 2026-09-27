using System.Threading.RateLimiting;

namespace securitycheck_portal.Auth;

/// <summary>
/// Caps failed binds per account, whatever the source IP, so password guessing cannot trip
/// the AD lockout policy. Only InvalidCredentials counts: that is what AD counts toward lockout.
/// </summary>
public sealed class FailedLoginThrottle : IDisposable
{
    public const int PermitLimit = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly PartitionedRateLimiter<string> _limiter = PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitLimit,
            Window = Window,
            QueueLimit = 0,
        }));

    public bool IsBlocked(string userName) =>
        _limiter.GetStatistics(Key(userName))?.CurrentAvailablePermits == 0;

    public void RecordFailure(string userName)
    {
        using var _ = _limiter.AttemptAcquire(Key(userName));
    }

    public void Dispose() => _limiter.Dispose();

    private static string Key(string userName) => userName.ToLowerInvariant();
}
