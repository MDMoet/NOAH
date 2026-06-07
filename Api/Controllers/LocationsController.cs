using Api.Helpers;
using Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NOAH.Contracts.Common;
using NOAH.Contracts.Locations;

// ReSharper disable once CheckNamespace
namespace NOAH.Api.Controllers;

/// <summary>
/// Exposes saved-location, distance, nearby place, and geocoding endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class LocationsController(ILocationsService locationsService, ILogger<LocationsController> logger)
    : ControllerBase
{
    private const int MaximumLocationQueryLength = 200;

    /// <summary>
    /// Saves the user's current location under a friendly name.
    /// </summary>
    /// <param name="request">The location details to save.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The created saved location.</returns>
    [HttpPost]
    public async Task<ActionResult<SavedLocationDto>> SaveCurrentLocationAsync(
        [FromBody] SaveCurrentLocationRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateSaveCurrentLocationRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        SavedLocationDto savedLocation = await locationsService.SaveCurrentLocationAsync(request!, cancellationToken);

        logger.LogInformation("Saved location with id {SavedLocationId}.", savedLocation.Id);

        return CreatedAtAction(
            "GetSavedLocationById",
            new { savedLocationId = savedLocation.Id },
            savedLocation);
    }

    /// <summary>
    /// Gets all saved locations.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The saved locations known to NOAH.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedLocationDto>>> GetSavedLocationsAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SavedLocationDto> savedLocations =
            await locationsService.GetSavedLocationsAsync(cancellationToken);

        return Ok(savedLocations);
    }

    /// <summary>
    /// Gets a saved location by id.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The saved location when found.</returns>
    [HttpGet("{savedLocationId:guid}", Name = "GetSavedLocationById")]
    public async Task<ActionResult<SavedLocationDto>> GetSavedLocationByIdAsync(
        Guid savedLocationId,
        CancellationToken cancellationToken)
    {
        if (savedLocationId == Guid.Empty)
        {
            return BadRequest("Saved location id cannot be empty.");
        }

        SavedLocationDto? savedLocation =
            await locationsService.GetSavedLocationByIdAsync(savedLocationId, cancellationToken);

        if (savedLocation == null)
        {
            return NotFound();
        }

        return Ok(savedLocation);
    }

    /// <summary>
    /// Fully updates an existing saved location.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to update.</param>
    /// <param name="request">The replacement saved-location details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The updated saved location when found.</returns>
    [HttpPut("{savedLocationId:guid}")]
    public async Task<ActionResult<SavedLocationDto>> UpdateSavedLocationAsync(
        Guid savedLocationId,
        [FromBody] UpdateSavedLocationRequest? request,
        CancellationToken cancellationToken)
    {
        if (savedLocationId == Guid.Empty)
        {
            return BadRequest("Saved location id cannot be empty.");
        }

        Dictionary<string, string[]> validationErrors = ValidateUpdateSavedLocationRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        SavedLocationDto? updatedSavedLocation = await locationsService.UpdateSavedLocationAsync(
            savedLocationId,
            request!,
            cancellationToken);

        if (updatedSavedLocation == null)
        {
            return NotFound();
        }

        logger.LogInformation("Updated saved location with id {SavedLocationId}.", savedLocationId);

        return Ok(updatedSavedLocation);
    }

    /// <summary>
    /// Deletes an existing saved location.
    /// </summary>
    /// <param name="savedLocationId">The unique identifier of the saved location to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>No content when the saved location was deleted.</returns>
    [HttpDelete("{savedLocationId:guid}")]
    public async Task<IActionResult> DeleteSavedLocationAsync(
        Guid savedLocationId,
        CancellationToken cancellationToken)
    {
        if (savedLocationId == Guid.Empty)
        {
            return BadRequest("Saved location id cannot be empty.");
        }

        bool savedLocationWasDeleted =
            await locationsService.DeleteSavedLocationAsync(savedLocationId, cancellationToken);

        if (!savedLocationWasDeleted)
        {
            return NotFound();
        }

        logger.LogInformation("Deleted saved location with id {SavedLocationId}.", savedLocationId);

        return NoContent();
    }

    /// <summary>
    /// Calculates the distance between two coordinates.
    /// </summary>
    /// <param name="request">The two coordinates used to calculate distance.</param>
    /// <returns>The calculated distance in kilometers.</returns>
    [HttpPost("distance")]
    public ActionResult<DistanceResponse> CalculateDistance([FromBody] DistanceRequest? request)
    {
        Dictionary<string, string[]> validationErrors = ValidateDistanceRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        DistanceResponse distance = locationsService.CalculateDistance(request!);

        return Ok(distance);
    }

    /// <summary>
    /// Finds live nearby places using the configured places provider.
    /// </summary>
    /// <param name="request">The nearby places search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The nearby places matching the search request.</returns>
    [HttpPost("nearby")]
    public async Task<ActionResult<NearbyPlacesResponse>> GetNearbyPlacesAsync(
        [FromBody] NearbyPlacesRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateNearbyPlacesRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        try
        {
            NearbyPlacesResponse nearbyPlaces = await locationsService.GetNearbyPlacesAsync(request!, cancellationToken);

            return Ok(nearbyPlaces);
        }
        catch (LocationProviderUnavailableException exception)
        {
            return LocationProviderUnavailableProblem(exception);
        }
    }

    /// <summary>
    /// Searches for coordinates by a human-readable location query.
    /// </summary>
    /// <param name="request">The geocoding query details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The geocoding results returned by the configured provider.</returns>
    [HttpPost("geocode")]
    public async Task<ActionResult<GeocodeResponse>> GeocodeAsync(
        [FromBody] GeocodeRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateGeocodeRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        try
        {
            GeocodeResponse geocodeResponse = await locationsService.GeocodeAsync(request!, cancellationToken);

            return Ok(geocodeResponse);
        }
        catch (LocationProviderUnavailableException exception)
        {
            return LocationProviderUnavailableProblem(exception);
        }
    }

    /// <summary>
    /// Finds a human-readable location for a coordinate.
    /// </summary>
    /// <param name="request">The reverse geocoding details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The reverse geocoded location when found.</returns>
    [HttpPost("reverse-geocode")]
    public async Task<ActionResult<ReverseGeocodeResponse>> ReverseGeocodeAsync(
        [FromBody] ReverseGeocodeRequest? request,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> validationErrors = ValidateReverseGeocodeRequest(request);

        if (validationErrors.Count > 0)
        {
            return ValidationProblem(ToModelStateDictionary(validationErrors));
        }

        try
        {
            ReverseGeocodeResponse? reverseGeocodeResponse =
                await locationsService.ReverseGeocodeAsync(request!, cancellationToken);

            if (reverseGeocodeResponse == null)
            {
                return NotFound();
            }

            return Ok(reverseGeocodeResponse);
        }
        catch (LocationProviderUnavailableException exception)
        {
            return LocationProviderUnavailableProblem(exception);
        }
    }

    /// <summary>
    /// Validates a save-current-location request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateSaveCurrentLocationRequest(
        SaveCurrentLocationRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateSavedLocationName(request.Name, validationErrors);
        ValidateAddress(request.Address, validationErrors);
        ValidateCoordinate(request.Coordinate, nameof(request.Coordinate), validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Validates an update-saved-location request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateUpdateSavedLocationRequest(
        UpdateSavedLocationRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateSavedLocationName(request.Name, validationErrors);
        ValidateAddress(request.Address, validationErrors);
        ValidateCoordinate(request.Coordinate, nameof(request.Coordinate), validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Validates a distance calculation request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateDistanceRequest(DistanceRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateCoordinate(request.From, nameof(request.From), validationErrors);
        ValidateCoordinate(request.To, nameof(request.To), validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Validates a nearby places request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateNearbyPlacesRequest(NearbyPlacesRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateCoordinate(request.Origin, nameof(request.Origin), validationErrors);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            validationErrors[nameof(request.Query)] = ["Query is required."];
        }
        else if (request.Query.Length > MaximumLocationQueryLength)
        {
            validationErrors[nameof(request.Query)] =
                [$"Query cannot be longer than {MaximumLocationQueryLength} characters."];
        }

        if (!double.IsFinite(request.RadiusKilometers) || request.RadiusKilometers <= 0)
        {
            validationErrors[nameof(request.RadiusKilometers)] = ["Radius kilometers must be greater than zero."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a geocoding request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateGeocodeRequest(GeocodeRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            validationErrors[nameof(request.Query)] = ["Query is required."];
        }
        else if (request.Query.Length > MaximumLocationQueryLength)
        {
            validationErrors[nameof(request.Query)] =
                [$"Query cannot be longer than {MaximumLocationQueryLength} characters."];
        }

        return validationErrors;
    }

    /// <summary>
    /// Validates a reverse geocoding request.
    /// </summary>
    /// <param name="request">The request to validate.</param>
    /// <returns>A dictionary of validation errors.</returns>
    private static Dictionary<string, string[]> ValidateReverseGeocodeRequest(ReverseGeocodeRequest? request)
    {
        Dictionary<string, string[]> validationErrors = new();

        if (request == null)
        {
            validationErrors["Request"] = ["Request body is required."];
            return validationErrors;
        }

        ValidateCoordinate(request.Coordinate, nameof(request.Coordinate), validationErrors);

        return validationErrors;
    }

    /// <summary>
    /// Adds validation errors for an invalid saved-location name.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateSavedLocationName(
        string? name,
        Dictionary<string, string[]> validationErrors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            validationErrors["Name"] = ["Name is required."];
            return;
        }

        if (name.Length > 200)
        {
            validationErrors["Name"] = ["Name cannot be longer than 200 characters."];
        }
    }

    /// <summary>
    /// Adds validation errors for an invalid address.
    /// </summary>
    /// <param name="address">The address to validate.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateAddress(
        string? address,
        Dictionary<string, string[]> validationErrors)
    {
        if (address is { Length: > 500 })
        {
            validationErrors[nameof(address)] = ["Address cannot be longer than 500 characters."];
        }
    }

    /// <summary>
    /// Adds validation errors for an invalid coordinate.
    /// </summary>
    /// <param name="coordinate">The coordinate to validate.</param>
    /// <param name="fieldName">The field name used in validation errors.</param>
    /// <param name="validationErrors">The validation error collection to update.</param>
    private static void ValidateCoordinate(
        GeoCoordinateDto? coordinate,
        string fieldName,
        Dictionary<string, string[]> validationErrors)
    {
        if (coordinate == null)
        {
            validationErrors[fieldName] = ["Coordinate is required."];
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

    /// <summary>
    /// Creates a standardized response for provider connectivity or configuration failures.
    /// </summary>
    /// <param name="exception">The provider exception that caused the failure.</param>
    /// <returns>A 503 problem response.</returns>
    private ObjectResult LocationProviderUnavailableProblem(LocationProviderUnavailableException exception)
    {
        logger.LogWarning(exception, "Location provider is unavailable.");

        return Problem(
            title: "Location provider unavailable.",
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
