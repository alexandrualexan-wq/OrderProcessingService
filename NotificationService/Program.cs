using Dapr.Client;
using Microsoft.EntityFrameworkCore;
using NotificationService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("NotificationDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<DaprSidecarHealthCheck>();
builder.Services.AddControllers().AddDapr();
var app = builder.Build();
app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapControllers();
app.MapGet("/healthz", () => "OK");
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
        _logger.LogInformation("Waiting for Dapr sidecar to be ready...");
        await _daprClient.WaitForSidecarAsync(stoppingToken);
        _logger.LogInformation("Dapr sidecar is ready.");
    }
}