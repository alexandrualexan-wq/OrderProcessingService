using Microsoft.EntityFrameworkCore;
using NotificationService;

// 1. CONFIGURE AND RUN THE WEB APPLICATION
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(opt => opt.UseInMemoryDatabase("NotificationDb"));

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
    public DbSet<Notification> Notifications { get; set; } = null!;
}

public class Notification
{
    public Guid NotificationId { get; set; }
    public Guid OrderId { get; set; }
    public string? Message { get; set; }
    public DateTime SentDate { get; set; }
}
