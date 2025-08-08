using Dapr.Client;
using Microsoft.EntityFrameworkCore;
using OrderService;

// 1. CONFIGURE AND RUN THE WEB APPLICATION
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("OrderDb"));
builder.Services.AddDaprClient();
builder.Services.AddHostedService<OrderGenerator>();

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapControllers();

app.Run();

// 2. DEFINE SERVICES AND CLASSES (Must come after top-level statements)

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Order> Orders { get; set; } = null!;
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

            // Publish the event
            await _daprClient.PublishEventAsync("pubsub", "orders", order, stoppingToken);
            _logger.LogInformation("Published Order: {OrderId}", order.OrderId);

            // Save to in-memory database
            using (var scope = _serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                dbContext.Orders.Add(order);
                await dbContext.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
