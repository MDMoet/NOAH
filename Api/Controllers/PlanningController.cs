using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using NOAH.Contracts.Planning;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class PlanningController(IPlanningService planningService) : ControllerBase
{
    [HttpGet("day/{date}")]
    public async Task<ActionResult<DayPlanDto>> GetDayPlanAsync(
        string date,
        [FromQuery] string? timeZoneId,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out DateOnly parsedDate, out ActionResult? errorResult))
        {
            return errorResult!;
        }

        try
        {
            DayPlanDto dayPlan = await planningService.GetDayPlanAsync(
                parsedDate,
                timeZoneId,
                cancellationToken);

            return Ok(dayPlan);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("today")]
    public async Task<ActionResult<DayPlanDto>> GetTodayPlanAsync(
        [FromQuery] string? timeZoneId,
        CancellationToken cancellationToken)
    {
        try
        {
            DayPlanDto dayPlan = await planningService.GetTodayPlanAsync(timeZoneId, cancellationToken);

            return Ok(dayPlan);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("week/{date}")]
    public async Task<ActionResult<PlanningPeriodDto>> GetWeekPlanAsync(
        string date,
        [FromQuery] string? timeZoneId,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out DateOnly parsedDate, out ActionResult? errorResult))
        {
            return errorResult!;
        }

        try
        {
            PlanningPeriodDto weekPlan = await planningService.GetWeekPlanAsync(
                parsedDate,
                timeZoneId,
                cancellationToken);

            return Ok(weekPlan);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<PlanningPeriodDto>> GetUpcomingPlanAsync(
        [FromQuery] int days = 7,
        [FromQuery] string? timeZoneId = null,
        CancellationToken cancellationToken = default)
    {
        if (days <= 0)
        {
            return BadRequest("Days must be greater than zero.");
        }

        try
        {
            PlanningPeriodDto upcomingPlan = await planningService.GetUpcomingPlanAsync(
                days,
                timeZoneId,
                cancellationToken);

            return Ok(upcomingPlan);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("overdue")]
    public async Task<ActionResult<PlanningItemsDto>> GetOverdueItemsAsync(
        [FromQuery] string? timeZoneId,
        CancellationToken cancellationToken)
    {
        try
        {
            PlanningItemsDto overdueItems = await planningService.GetOverdueItemsAsync(
                timeZoneId,
                cancellationToken);

            return Ok(overdueItems);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    [HttpGet("backlog")]
    public async Task<ActionResult<PlanningItemsDto>> GetBacklogItemsAsync(
        [FromQuery] string? timeZoneId,
        CancellationToken cancellationToken)
    {
        try
        {
            PlanningItemsDto backlogItems = await planningService.GetBacklogItemsAsync(
                timeZoneId,
                cancellationToken);

            return Ok(backlogItems);
        }
        catch (TimeZoneNotFoundException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidTimeZoneException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private bool TryParseDate(string date, out DateOnly parsedDate, out ActionResult? errorResult)
    {
        if (DateOnly.TryParseExact(date, "yyyy-MM-dd", out parsedDate))
        {
            errorResult = null;
            return true;
        }

        errorResult = BadRequest("Date must use the yyyy-MM-dd format.");
        return false;
    }
}
