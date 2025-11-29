using HealteCheck.Services;
using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using HealthCheck.Exporter;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// קונפיג לתוך HealthConfigRoot
var healthConfig = new HealthConfigRoot();
builder.Configuration.Bind(healthConfig);
builder.Services.AddSingleton(healthConfig);

// שירותים מה-Core
builder.Services.AddSingleton<IShareStressRunner, ShareStressRunner>();
builder.Services.AddSingleton<IShareHealthCheckerFactory, ShareHealthCheckerFactory>();

// Updater למטריקות
builder.Services.AddSingleton<IHealthMetricsUpdater, HealthMetricsUpdater>();

// Controllers + HostedService
builder.Services.AddControllers();
builder.Services.AddHostedService<HealthMetricsBackgroundService>();

builder.Services.AddRouting();

var app = builder.Build();

// חשוב: קודם HTTP metrics, אחר כך Controllers/metrics endpoint
app.UseHttpMetrics();

app.MapControllers();          // /api/...
app.MapMetrics("/metrics");    // /metrics ל-Prometheus

// להאזין על 5000 לכל ה-interfaces (כדי שדוקר יראה אותך)
app.Urls.Add("http://0.0.0.0:5000");

app.Run();
