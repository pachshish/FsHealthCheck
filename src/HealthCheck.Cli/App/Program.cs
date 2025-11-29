using HealthCheck.Cli.App;

namespace HealthCheck.Cli;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var app = new AppHost();
        return await app.RunAsync(args);
    }
}
