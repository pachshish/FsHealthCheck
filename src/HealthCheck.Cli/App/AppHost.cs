using HealthCheck.Core.Models;

namespace HealthCheck.Cli.App;

public sealed class AppHost
{
    public async Task<int> RunAsync(string[] args)
    {
        // ה־exe שלך רץ מתוך bin\Debug\net9.0
        string baseDir = AppContext.BaseDirectory;

        // שם נחפש את appsettings.json (נדאג שיעתיקו אותו לשם)
        string defaultConfig = Path.Combine(baseDir, "appsettings.json");

        // אם נותנים נתיב בקומנד ליין – משתמשים בו, אחרת ברירת מחדל
        string configPath = args.Length > 0 ? args[0] : defaultConfig;

        var runner = new HealthRunner();
        return await runner.RunOnceAsync(configPath);
    }
}
