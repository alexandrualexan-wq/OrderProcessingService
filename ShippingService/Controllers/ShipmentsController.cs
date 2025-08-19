using Dapr;
using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.CircuitBreaker;
using System.Diagnostics;

namespace ShippingService.Controllers;

[ApiController]
[Route("[controller]")]
public class ShipmentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ShipmentsController> _logger;
    private readonly TelemetryClient _telemetryClient;
    private static readonly AsyncCircuitBreakerPolicy _circuitBreakerPolicy = Policy.Handle<Exception>().CircuitBreakerAsync(5, TimeSpan.FromSeconds(60));

    public ShipmentsController(AppDbContext dbContext, ILogger<ShipmentsController> logger, TelemetryClient telemetryClient)
    {
        _dbContext = dbContext;
        _logger = logger;
        _telemetryClient = telemetryClient;
    }

    [Topic("redis-pubsub", "orders")]
    [HttpPost("/orders")] // Route for Dapr to post to
    public async Task<IActionResult> CreateShipmentFromOrder(Order order)
    {
        var activity = new Activity("ProcessShipment").Start();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (await _dbContext.Shipments.AnyAsync(s => s.OrderId == order.OrderId))
            {
                _logger.LogWarning("Shipment for order {OrderId} already exists.", order.OrderId);
                return Ok(); // Idempotency check
            }

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

            await dbRetryPolicy.WrapAsync(_circuitBreakerPolicy).ExecuteAsync(async () =>
            {
                _dbContext.Shipments.Add(shipment);
                await _dbContext.SaveChangesAsync();
            });

            _logger.LogInformation("Created Shipment: {ShipmentId} for Order: {OrderId}", shipment.ShipmentId, order.OrderId);
            stopwatch.Stop();
            _telemetryClient.TrackMetric("ShipmentProcessingDuration", stopwatch.ElapsedMilliseconds);

            return Ok();
        }
        catch (BrokenCircuitException)
        {
            _logger.LogError("Database circuit breaker is open. Could not process shipment for order: {OrderId}", order.OrderId);
            return Problem("Database is unavailable.", statusCode: 503);
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

    [Topic("redis-pubsub", "dead-letter-queue")]
    [HttpPost("/dead-letter-queue")]
    public IActionResult HandleDeadLetter(object message)
    {
        _logger.LogError("Received message from dead-letter queue: {Message}", message);
        _telemetryClient.TrackEvent("DeadLetterMessageReceived", new Dictionary<string, string> { { "message", message?.ToString() ?? "null" } });
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _dbContext.Shipments.ToListAsync());
    }
}
