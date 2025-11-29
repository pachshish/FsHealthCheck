using HealthCheck.Core.Models;

namespace HealthCheck.Core.Services;

public interface IShareHealthCheckerFactory
{
    IShareHealthChecker Create(ShareHealthConfig cfg);
}
