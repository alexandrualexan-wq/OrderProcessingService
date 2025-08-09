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

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        return Ok(await _dbContext.Shipments.ToListAsync());
    }
}

