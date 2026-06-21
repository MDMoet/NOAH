using Microsoft.AspNetCore.Mvc;
using NOAH.Infrastructure.Persistence;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class DatabaseController : ControllerBase
{
    [HttpGet("test")]
    public async Task<IActionResult> TestDatabaseAsync([FromServices] NoahDbContext noahDbContext)
    {
        var canConnect = await noahDbContext.Database.CanConnectAsync();
        return Ok(new { canConnect });
    }
}
