using System.Threading.RateLimiting;
using Limescape.Umbraco.OaiPmh.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Limescape.Umbraco.OaiPmh.Endpoint;

/// <summary>Partitioned per remote IP; trusts only addresses established by the host's forwarded-header policy.</summary>
internal sealed class RateLimiting(IOptions<OaiPmhOptions> options) : IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.Value.RequestsPerSecond,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    public RateLimitLease Acquire(HttpContext context) => _limiter.AttemptAcquire(context);
    public void Dispose() => _limiter.Dispose();
}
