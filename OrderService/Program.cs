using Dapr.Client;
using Microsoft.EntityFrameworkCore;
using OrderService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("OrderDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<OrderGenerator>();
builder.Services.AddHostedService<DaprSidecarHealthCheck>();
builder.Services.AddControllers().AddDapr();
var app = builder.Build();
app.UseCloudEvents();
app.MapControllers();
app.MapGet("/healthz", () => "OK");
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


    public OrderGenerator(ILogger<OrderGenerator> logger, DaprClient daprClient, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _daprClient = daprClient;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The DaprSidecarHealthCheck will wait for the sidecar to be ready.
        // This service can start publishing messages immediately.
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


            // Publish the event with retry logic
            const int maxRetries = 3;
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    _logger.LogInformation("Publishing order: {OrderId}", order.OrderId);
                    await _daprClient.PublishEventAsync("pubsub", "orders", order, stoppingToken);
                    _logger.LogInformation("Published Order: {OrderId}", order.OrderId);


                    // Save to in-memory database
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        dbContext.Orders.Add(order);
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                    break; // Success, exit the retry loop
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing order: {OrderId}. Retry {RetryCount}/{MaxRetries}", order.OrderId, i + 1, maxRetries);
                    if (i < maxRetries - 1)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); // Wait before retrying
                    }
                    else
                    {
                        _logger.LogError("Failed to publish order: {OrderId} after {MaxRetries} retries.", order.OrderId, maxRetries);
                    }
                }
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
        _logger.LogInformation("Waiting for Dapr sidecar to be ready...");
        await _daprClient.WaitForSidecarAsync(stoppingToken);
        _logger.LogInformation("Dapr sidecar is ready.");
    }
}