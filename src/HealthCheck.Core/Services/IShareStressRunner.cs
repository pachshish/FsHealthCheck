using HealthCheck.Core.Models;
namespace HealteCheck.Services;

public interface IShareStressRunner
{
    Task RunStressAsync(ShareHealthConfig cfg, StressSettings settings, CancellationToken ct = default);
}

