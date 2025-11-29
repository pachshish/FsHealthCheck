using HealthCheck.Core.Models;
using HealthCheck.Services;

namespace HealthCheck.Core.Services;

public sealed class ShareHealthCheckerFactory : IShareHealthCheckerFactory
{
    public IShareHealthChecker Create(ShareHealthConfig cfg)
        => new ShareHealthChecker(cfg);
}
