using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ShippingService.Controllers;

[ApiController]
[Route("[controller]")]
public class ShipmentsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ShipmentsController> _logger;

    public ShipmentsController(AppDbContext dbContext, ILogger<ShipmentsController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [Topic("pubsub", "orders")]
    [HttpPost("/orders")] // Route for Dapr to post to
    public async Task<IActionResult> CreateShipmentFromOrder(Order order)
    {
        _logger.LogInformation("Received Order: {OrderId}", order.OrderId);

        var shipment = new Shipment
        {
            ShipmentId = Guid.NewGuid(),
            OrderId = order.OrderId,
            Status = "Preparing",
            CreatedDate = DateTime.UtcNow
        };

        _dbContext.Shipments.Add(shipment);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Created Shipment: {ShipmentId} for Order: {OrderId}", shipment.ShipmentId, order.OrderId);
        return Ok();
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _dbContext.Shipments.ToListAsync());
    }
}
