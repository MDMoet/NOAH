using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Common;
using NOAH.Contracts.Mileage;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Exposes mileage entry, mileage history, and summary endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class MileageController(IMileageService mileageService, ILogger<MileageController> logger)
    : ControllerBase
{
    private const int DefaultTake = 100;
    private const int MaximumTake = 500;
    private const int MaximumSourceImagePathLength = 1024;
    private const int MaximumRecognizedTextLength = 10_000;
    private const int MaximumNotesLength = 2000;
    private const decimal MaximumMileageValueKm = 9_999_999_999_999_999.99m;

    /// <summary>
    /// Creates a new mileage entry.
    /// </summary>
    /// <param name="request">The mileage details to save.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created mileage entry.</returns>
    [HttpPost]
    public async Task<ActionResult<MileageEntryDto>> CreateMileageEntryAsync(
        [FromBody] CreateMileageEntryRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateCreateMileageEntryRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        MileageEntryDto createdMileageEntry =
            await mileageService.CreateMileageEntryAsync(request!, cancellationToken);

        logger.LogInformation("Created mileage entry with id {MileageEntryId}.", createdMileageEntry.Id);

        return CreatedAtAction(
            "GetMileageEntryById",
            new { mileageEntryId = createdMileageEntry.Id },
            createdMileageEntry);
    }

    /// <summary>
    /// Gets mileage entries, optionally filtered by recorded-at range.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching mileage entries.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MileageEntryDto>>> GetMileageEntriesAsync(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        [FromQuery] int take = DefaultTake,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, string[]> validationErrors = ValidateMileageQuery(fromUtc, toUtc, take);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        IReadOnlyList<MileageEntryDto> mileageEntries =
            await mileageService.GetMileageEntriesAsync(fromUtc, toUtc, take, cancellationToken);

        return Ok(mileageEntries);
    }

    /// <summary>
    /// Gets the newest mileage entry.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The latest mileage entry when one exists.</returns>
    [HttpGet("latest")]
    public async Task<ActionResult<MileageEntryDto>> GetLatestMileageEntryAsync(
        CancellationToken cancellationToken)
    {
        MileageEntryDto? mileageEntry = await mileageService.GetLatestMileageEntryAsync(cancellationToken);

        if (mileageEntry == null)
        {
            return NotFound();
        }

        return Ok(mileageEntry);
    }

    /// <summary>
    /// Gets aggregate mileage statistics for an optional recorded-at range.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage summary.</returns>
    [HttpGet("summary")]
    public async Task<ActionResult<MileageSummaryDto>> GetMileageSummaryAsync(
        [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateDateRange(fromUtc, toUtc);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        MileageSummaryDto mileageSummary =
            await mileageService.GetMileageSummaryAsync(fromUtc, toUtc, cancellationToken);

        return Ok(mileageSummary);
    }

    /// <summary>
    /// Gets a mileage entry by id.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The mileage entry when found.</returns>
    [HttpGet("{mileageEntryId:guid}", Name = "GetMileageEntryById")]
    public async Task<ActionResult<MileageEntryDto>> GetMileageEntryByIdAsync(
        Guid mileageEntryId,
        CancellationToken cancellationToken)
    {
        if (mileageEntryId == Guid.Empty)
        {
            return BadRequest("Mileage entry id cannot be empty.");
        }

        MileageEntryDto? mileageEntry =
            await mileageService.GetMileageEntryByIdAsync(mileageEntryId, cancellationToken);

        if (mileageEntry == null)
        {
            return NotFound();
        }

        return Ok(mileageEntry);
    }

    /// <summary>
    /// Fully updates an existing mileage entry.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to update.</param>
    /// <param name="request">The replacement mileage details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated mileage entry when found.</returns>
    [HttpPut("{mileageEntryId:guid}")]
    public async Task<ActionResult<MileageEntryDto>> UpdateMileageEntryAsync(
        Guid mileageEntryId,
        [FromBody] UpdateMileageEntryRequest? request,
        CancellationToken cancellationToken)
    {
        if (mileageEntryId == Guid.Empty)
        {
            return BadRequest("Mileage entry id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateMileageEntryRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        MileageEntryDto? updatedMileageEntry = await mileageService.UpdateMileageEntryAsync(
            mileageEntryId,
            request!,
            cancellationToken);

        if (updatedMileageEntry == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated mileage entry with id {MileageEntryId}.", mileageEntryId);

        return Ok(updatedMileageEntry);
    }

    /// <summary>
    /// Deletes an existing mileage entry.
    /// </summary>
    /// <param name="mileageEntryId">The unique identifier of the mileage entry to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content when the mileage entry was deleted.</returns>
    [HttpDelete("{mileageEntryId:guid}")]
    public async Task<IActionResult> DeleteMileageEntryAsync(
        Guid mileageEntryId,
        CancellationToken cancellationToken)
    {
        if (mileageEntryId == Guid.Empty)
        {
            return BadRequest("Mileage entry id cannot be empty.");
        }

        bool mileageEntryWasDeleted =
            await mileageService.DeleteMileageEntryAsync(mileageEntryId, cancellationToken);

        if (!mileageEntryWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted mileage entry with id {MileageEntryId}.", mileageEntryId);

        return NoContent();
    }

    /// <summary>
    /// Validates a create-mileage-entry request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateCreateMileageEntryRequest(
        CreateMileageEntryRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateRecordedAtUtc(request.RecordedAtUtc, validationErrors);
        ValidateMileageValues(request.OdometerReadingKm, request.TripDistanceKm, validationErrors);
        ValidateSource(request.Source, validationErrors);
        ValidateOptionalText(request.SourceImagePath, nameof(request.SourceImagePath), MaximumSourceImagePathLength, validationErrors);
        ValidateOptionalText(request.RecognizedText, nameof(request.RecognizedText), MaximumRecognizedTextLength, validationErrors);
        ValidateOptionalText(request.CorrectedText, nameof(request.CorrectedText), MaximumRecognizedTextLength, validationErrors);
        ValidateOptionalCoordinate(request.Location, nameof(request.Location), validationErrors);
        ValidateOptionalText(request.Notes, nameof(request.Notes), MaximumNotesLength, validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Validates an update-mileage-entry request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateUpdateMileageEntryRequest(
        UpdateMileageEntryRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateRecordedAtUtc(request.RecordedAtUtc, validationErrors);
        ValidateMileageValues(request.OdometerReadingKm, request.TripDistanceKm, validationErrors);
        ValidateOptionalText(request.CorrectedText, nameof(request.CorrectedText), MaximumRecognizedTextLength, validationErrors);
        ValidateOptionalCoordinate(request.Location, nameof(request.Location), validationErrors);
        ValidateOptionalText(request.Notes, nameof(request.Notes), MaximumNotesLength, validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Validates history query parameters.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateMileageQuery(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int take)
    {
        Dictionary<string, string[]> validationErrors = ValidateDateRange(fromUtc, toUtc);

        if (take is < 1 or > MaximumTake)
        {
            validationErrors[nameof(take)] = [$"Take must be between 1 and {MaximumTake}."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates an optional recorded-at range.
    /// </summary>
    /// <param name="fromUtc">The optional inclusive lower recorded-at boundary.</param>
    /// <param name="toUtc">The optional inclusive upper recorded-at boundary.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateDateRange(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        {
            validationErrors[nameof(fromUtc)] = ["From UTC must be before or equal to To UTC."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Adds validation errors for an invalid recorded-at timestamp.
    /// </summary>
    /// <param name="recordedAtUtc">The recorded-at timestamp to validate.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateRecordedAtUtc(
        DateTimeOffset recordedAtUtc,
        Dictionary<string, string[]> validationErrors)
    {
        if (recordedAtUtc == default)
        {
            validationErrors[nameof(recordedAtUtc)] = ["Recorded at UTC is required."];
        }
    }

    /// <summary>
    /// Adds validation errors for invalid mileage values.
    /// </summary>
    /// <param name="odometerReadingKm">The odometer reading to validate.</param>
    /// <param name="tripDistanceKm">The optional trip distance to validate.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateMileageValues(
        decimal odometerReadingKm,
        decimal? tripDistanceKm,
        Dictionary<string, string[]> validationErrors)
    {
        if (odometerReadingKm < 0)
        {
            validationErrors[nameof(odometerReadingKm)] = ["Odometer reading kilometers must be greater than or equal to zero."];
        }
        else if (odometerReadingKm > MaximumMileageValueKm)
        {
            validationErrors[nameof(odometerReadingKm)] =
                [$"Odometer reading kilometers must be less than or equal to {MaximumMileageValueKm}."];
        }

        if (tripDistanceKm.HasValue && tripDistanceKm.Value < 0)
        {
            validationErrors[nameof(tripDistanceKm)] = ["Trip distance kilometers must be greater than or equal to zero."];
        }
        else if (tripDistanceKm.HasValue && tripDistanceKm.Value > MaximumMileageValueKm)
        {
            validationErrors[nameof(tripDistanceKm)] =
                [$"Trip distance kilometers must be less than or equal to {MaximumMileageValueKm}."];
        }
    }

    /// <summary>
    /// Adds validation errors for an invalid mileage-entry source.
    /// </summary>
    /// <param name="source">The source to validate.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateSource(
        NOAH.Contracts.Enums.MileageEntrySourceDto source,
        Dictionary<string, string[]> validationErrors)
    {
        if (!Enum.IsDefined(source))
        {
            validationErrors[nameof(source)] = ["Source is invalid."];
        }
    }

    /// <summary>
    /// Adds validation errors for optional text length.
    /// </summary>
    /// <param name="value">The text to validate.</param>
    /// <param name="fieldName">The field name used in validation errors.</param>
    /// <param name="maximumLength">The maximum allowed text length.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateOptionalText(
        string? value,
        string fieldName,
        int maximumLength,
        Dictionary<string, string[]> validationErrors)
    {
        if (value is { Length: > 0 } && value.Trim().Length > maximumLength)
        {
            validationErrors[fieldName] = [$"{fieldName} cannot be longer than {maximumLength} characters."];
        }
    }

    /// <summary>
    /// Adds validation errors for an invalid optional coordinate.
    /// </summary>
    /// <param name="coordinate">The coordinate to validate.</param>
    /// <param name="fieldName">The field name used in validation errors.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateOptionalCoordinate(
        GeoCoordinateDto? coordinate,
        string fieldName,
        Dictionary<string, string[]> validationErrors)
    {
        if (coordinate == null)
        {
            return;
        }

        if (!double.IsFinite(coordinate.Latitude) || coordinate.Latitude is < -90 or > 90)
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.Latitude)}"] =
                ["Latitude must be between -90 and 90."];
        }

        if (!double.IsFinite(coordinate.Longitude) || coordinate.Longitude is < -180 or > 180)
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.Longitude)}"] =
                ["Longitude must be between -180 and 180."];
        }

        if (coordinate.AccuracyMeters.HasValue &&
            (!double.IsFinite(coordinate.AccuracyMeters.Value) || coordinate.AccuracyMeters < 0))
        {
            validationErrors[$"{fieldName}.{nameof(coordinate.AccuracyMeters)}"] =
                ["Accuracy meters must be greater than or equal to zero."];
        }
    }

    /// <summary>
    /// Converts validation errors into ASP.NET model state.
    /// </summary>
    /// <param name="validationErrors">The validation errors to convert.</param>
    /// <returns>The model state containing the validation errors.</returns>
    private static ModelStateDictionary ToModelStateDictionary(
        Dictionary<string, string[]> validationErrors)
    {
        ModelStateDictionary modelStateDictionary = new();

        foreach (KeyValuePair<string, string[]> validationError in validationErrors)
        {
            foreach (string errorMessage in validationError.Value)
            {
                modelStateDictionary.AddModelError(validationError.Key, errorMessage);
            }
        }

        return modelStateDictionary;
    }

}
