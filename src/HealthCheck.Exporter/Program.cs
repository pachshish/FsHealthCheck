using HealteCheck.Services;
using HealthCheck.Core.Models;
using HealthCheck.Core.Services;
using HealthCheck.Exporter;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// config for HealthConfigRoot
var healthConfig = new HealthConfigRoot();
builder.Configuration.Bind(healthConfig);
builder.Services.AddSingleton(healthConfig);

// services from the Core
builder.Services.AddSingleton<IShareStressRunner, ShareStressRunner>();
builder.Services.AddSingleton<IShareHealthCheckerFactory, ShareHealthCheckerFactory>();

// Updater for metrics, used by the BackgroundService and the Controller
builder.Services.AddSingleton<IHealthMetricsUpdater, HealthMetricsUpdater>();

// Controllers + HostedService
builder.Services.AddControllers();
builder.Services.AddHostedService<HealthMetricsBackgroundService>();

builder.Services.AddRouting();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpMetrics();

app.MapControllers();
app.MapMetrics("/metrics");

app.Urls.Add("http://0.0.0.0:5000");

app.Run();