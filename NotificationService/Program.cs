using Dapr.Client;
using Microsoft.ApplicationInsights;
using Microsoft.EntityFrameworkCore;
using NotificationService;

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("NotificationDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<DaprSidecarHealthCheck>();
builder.Services.AddControllers().AddDapr();

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapControllers();
app.MapGet("/healthz", () => "OK");

app.Lifetime.ApplicationStarted.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("NotificationService started at {Timestamp}", DateTime.UtcNow);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("NotificationService stopping at {Timestamp}", DateTime.UtcNow);
});

app.Run();

// 2. DEFINE MODELS AND DBCONTEXT (Must come after top-level statements)

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Notification> Notifications { get; set; } = null!;
}

public class Notification
{
    public Guid NotificationId { get; set; }
    public Guid OrderId { get; set; }
    public string? Message { get; set; }
    public DateTime SentDate { get; set; }
}

public class DaprSidecarHealthCheck : BackgroundService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprSidecarHealthCheck> _logger;

    public DaprSidecarHealthCheck(DaprClient daprClient, ILogger<DaprSidecarHealthCheck> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _daprClient.WaitForSidecarAsync(stoppingToken);
                _logger.LogInformation("Dapr sidecar is ready.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dapr sidecar is not ready yet. Retrying in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}