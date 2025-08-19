using Dapr;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly;
using System.Diagnostics;

namespace ShippingService.Controllers;

[ApiController]
[Route("[controller]")]
public class ShipmentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ShipmentsController> _logger;
    private readonly TelemetryClient _telemetryClient;

    public ShipmentsController(AppDbContext dbContext, ILogger<ShipmentsController> logger, TelemetryClient telemetryClient)
    {
        _dbContext = dbContext;
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    [Topic("pubsub", "orders")]
    [HttpPost("/orders")] // Route for Dapr to post to
    public async Task<IActionResult> CreateShipmentFromOrder(Order order)
    {
        var activity = new Activity("ProcessShipment").Start();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogInformation("Received Order: {OrderId}", order.OrderId);
            _telemetryClient.TrackEvent("OrderReceived", new Dictionary<string, string> { { "topic", "orders" }, { "orderId", order.OrderId.ToString() } });

            var shipment = new Shipment
            {
                ShipmentId = Guid.NewGuid(),
                OrderId = order.OrderId,
                Status = "Preparing",
                CreatedDate = DateTime.UtcNow
            };

            var dbRetryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(retryAttempt), (ex, time) =>
                {
                    _logger.LogWarning(ex, "Error saving shipment to database. Retrying in {Time}s", time.TotalSeconds);
                });

            await dbRetryPolicy.ExecuteAsync(async () =>
            {
                _dbContext.Shipments.Add(shipment);
                await _dbContext.SaveChangesAsync();
            });

            _logger.LogInformation("Created Shipment: {ShipmentId} for Order: {OrderId}", shipment.ShipmentId, order.OrderId);
            stopwatch.Stop();
            _telemetryClient.TrackMetric("ShipmentProcessingDuration", stopwatch.ElapsedMilliseconds);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing shipment for order: {OrderId}", order.OrderId);
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
        return Ok(await _dbContext.Shipments.ToListAsync());
    }
}
