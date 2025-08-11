using Dapr.Client;
using Microsoft.EntityFrameworkCore;
using ShippingService;

// 1. CONFIGURE AND RUN THE WEB APPLICATION
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("ShippingDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<DaprSidecarHealthCheck>();

// Add Dapr integration for controllers
builder.Services.AddControllers().AddDapr();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseCloudEvents();
app.MapSubscribeHandler();
app.MapControllers();

app.MapGet("/healthz", () => "OK");

app.Run();

// 2. DEFINE MODELS AND DBCONTEXT (Must come after top-level statements)

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Shipment> Shipments { get; set; } = null!;
}

public class Shipment
{
    public Guid ShipmentId { get; set; }
    public Guid OrderId { get; set; }
    public string? Status { get; set; }
    public DateTime CreatedDate { get; set; }
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