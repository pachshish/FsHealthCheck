using HealthCheck.Core.Models;

namespace HealthCheck.Core.Services;

public interface IShareHealthChecker
{
    Task<ShareHealthResult> RunChecksAsync();
}
