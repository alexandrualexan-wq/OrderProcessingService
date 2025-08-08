using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace NotificationService.Controllers;

[ApiController]
[Route("[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(AppDbContext dbContext, ILogger<NotificationsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [Topic("pubsub", "orders", new[] { "notification-service" })]
    [HttpPost("/orders")] // Route for Dapr to post to
    public async Task<IActionResult> CreateNotificationFromOrder(Order order)
    {
        _logger.LogInformation("Received Order: {OrderId}", order.OrderId);

        var notification = new Notification
        {
            NotificationId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Message = $"Your order {order.OrderId} has been received.",
            SentDate = DateTime.UtcNow
        };

        _dbContext.Notifications.Add(notification);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Sent notification for Order: {OrderId}", order.OrderId);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _dbContext.Notifications.ToListAsync());
    }
}
