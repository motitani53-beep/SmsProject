using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsGateway.Shared.Data;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly SmscStatusStore _smscStatusStore;

    public StatusController(ApplicationDbContext context, SmscStatusStore smscStatusStore)
    {
        _context = context;
        _smscStatusStore = smscStatusStore;
    }

    /// <summary>
    /// Returns the latest SMPP provider (SMSC) connectivity status from the SmscStatus table.
    /// </summary>
    [HttpGet("provider")]
    public async Task<IActionResult> GetProviderStatus()
    {
        var latest = await _context.SmscStatus.FindAsync(1);

        if (latest == null)
        {
            return Ok(new { status = (string?)null, timestamp = (DateTime?)null });
        }

        return Ok(new
        {
            status = latest.Status,
            timestamp = latest.Timestamp
        });
    }

    /// <summary>
    /// Returns current SMSC status and reason from the in-memory store (synced with DB on transitions).
    /// </summary>
    [HttpGet("smsc")]
    public IActionResult GetSmscStatus()
    {
        return Ok(new
        {
            status = _smscStatusStore.CurrentStatus,
            reason = _smscStatusStore.LastReason
        });
    }
}
