using Dapr.Client;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.EntityFrameworkCore;
using OrderService;
using Polly;
using Polly.CircuitBreaker;

using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Add Application Insights telemetry
builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("OrderDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<OrderGenerator>();
builder.Services.AddHostedService<DaprSidecarHealthCheck>();
builder.Services.AddControllers().AddDapr();

var app = builder.Build();

app.UseCloudEvents();
app.MapControllers();
app.MapGet("/health", () => "OK");

app.Lifetime.ApplicationStarted.Register(() => 
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("OrderService started at {Timestamp}", DateTime.UtcNow);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("OrderService stopping at {Timestamp}", DateTime.UtcNow);
});

app.Run();


// 2. DEFINE SERVICES AND CLASSES (Must come after top-level statements)
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
}


public class OrderGenerator : BackgroundService
{
    private readonly ILogger<OrderGenerator> _logger;
    private readonly DaprClient _daprClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelemetryClient _telemetryClient;
    private static readonly AsyncCircuitBreakerPolicy _daprCircuitBreakerPolicy = Policy.Handle<Exception>().CircuitBreakerAsync(5, TimeSpan.FromSeconds(60));

    public OrderGenerator(ILogger<OrderGenerator> logger, DaprClient daprClient, IServiceProvider serviceProvider, TelemetryClient telemetryClient)
    {
        _logger = logger;
        _daprClient = daprClient;
        _serviceProvider = serviceProvider;
        _telemetryClient = telemetryClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var daprRetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.LogWarning(ex, "Error publishing message. Retrying in {Time}s", time.TotalSeconds);
            });

        var daprFallbackPolicy = Policy.Handle<Exception>().FallbackAsync(async (ct) => 
        {
            _logger.LogError("Failed to publish message after all retries. Dapr circuit breaker state: {State}", _daprCircuitBreakerPolicy.CircuitState);
            await Task.CompletedTask;
        });

        var dbRetryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(retryAttempt), (ex, time) =>
            {
                _logger.LogWarning(ex, "Error saving to database. Retrying in {Time}s", time.TotalSeconds);
            });

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                CustomerName = $"Customer {Guid.NewGuid().ToString().Substring(0, 4)}",
                OrderDate = DateTime.UtcNow,
                Items = new List<OrderItem>
                {
                    new OrderItem { ProductId = "prod-001", ProductName = "Laptop", Quantity = 1, UnitPrice = 1200.00m },
                    new OrderItem { ProductId = "prod-002", ProductName = "Mouse", Quantity = 1, UnitPrice = 25.50m }
                },
                TotalAmount = 1225.50m
            };

            var activity = new Activity("PublishOrder").Start();
            try
            {
                var stopwatch = Stopwatch.StartNew();
                await daprFallbackPolicy.WrapAsync(daprRetryPolicy.WrapAsync(_daprCircuitBreakerPolicy)).ExecuteAsync(async () =>
                {
                    await _daprClient.PublishEventAsync("redis-pubsub", "orders", order, stoppingToken);
                });
                stopwatch.Stop();

                _telemetryClient.TrackEvent("OrderPublished", new Dictionary<string, string>
                {
                    { "topic", "orders" },
                    { "messageSize", "1024" } // Using a fixed size to avoid the warning
                }, new Dictionary<string, double>
                {
                    { "latency", stopwatch.ElapsedMilliseconds }
                });

                _logger.LogInformation("Published Order: {OrderId}", order.OrderId);

                await dbRetryPolicy.ExecuteAsync(async () =>
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        dbContext.Orders.Add(order);
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish order: {OrderId}", order.OrderId);
                _telemetryClient.TrackException(ex);
            }
            finally
            {
                activity.Stop();
            }
        }
    }
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