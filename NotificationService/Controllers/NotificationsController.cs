using Dapr;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly;
using System.Diagnostics;

namespace NotificationService.Controllers;

[ApiController]
[Route("[controller]")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<NotificationsController> _logger;
    private readonly TelemetryClient _telemetryClient;

    public NotificationsController(AppDbContext dbContext, ILogger<NotificationsController> logger, TelemetryClient telemetryClient)
    {
        _dbContext = dbContext;
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    [Topic("pubsub", "orders")]
    [HttpPost("/orders")] // Route for Dapr to post to
    public async Task<IActionResult> CreateNotificationFromOrder(Order order)
    {
        var activity = new Activity("ProcessNotification").Start();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("Received Order: {OrderId}", order.OrderId);
            _telemetryClient.TrackEvent("OrderReceived", new Dictionary<string, string> { { "topic", "orders" }, { "orderId", order.OrderId.ToString() } });

            var notification = new Notification
            {
                NotificationId = Guid.NewGuid(),
                OrderId = order.OrderId,
                Message = $"Your order {order.OrderId} has been received.",
                SentDate = DateTime.UtcNow
            };

            var dbRetryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(retryAttempt), (ex, time) =>
                {
                    _logger.LogWarning(ex, "Error saving notification to database. Retrying in {Time}s", time.TotalSeconds);
                });

            await dbRetryPolicy.ExecuteAsync(async () =>
            {
                _dbContext.Notifications.Add(notification);
                await _dbContext.SaveChangesAsync();
            });

            _logger.LogInformation("Sent notification for Order: {OrderId}", order.OrderId);
            stopwatch.Stop();
            _telemetryClient.TrackMetric("NotificationProcessingDuration", stopwatch.ElapsedMilliseconds);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notification for order: {OrderId}", order.OrderId);
            _telemetryClient.TrackException(ex);
            return Problem(ex.Message, statusCode: 500);
        }
        finally
        {
            activity.Stop();
        }
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _dbContext.Notifications.ToListAsync());
    }
}
