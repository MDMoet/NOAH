using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Helpers;
using Api.Interfaces.Providers;
using Api.Options;
using Microsoft.Extensions.Options;
using NOAH.Contracts.Common;
using NOAH.Contracts.Locations;

namespace Api.Services.Providers;

/// <summary>
/// Uses a configured Nominatim instance to geocode and reverse geocode OpenStreetMap locations.
/// </summary>
public sealed class NominatimGeocodingProvider(
    HttpClient httpClient,
    IOptions<OpenStreetMapModel> openStreetMapOptions)
    : IGeocodingProvider
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NominatimOptions _options = openStreetMapOptions.Value.Nominatim;

    /// <summary>
    /// Searches for locations matching a human-readable query.
    /// </summary>
    /// <param name="request">The geocoding search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The geocoding results returned by Nominatim.</returns>
    public async Task<GeocodeResponse> GeocodeAsync(
        GeocodeRequest request,
        CancellationToken cancellationToken = default)
    {
        Uri requestUri = BuildUrl("search", new Dictionary<string, string>
        {
            ["format"] = "jsonv2",
            ["q"] = request.Query.Trim(),
            ["limit"] = GetMaxResults().ToString(CultureInfo.InvariantCulture),
            // Address details give us structured fields for a compact normalized address.
            ["addressdetails"] = "1"
        });

        using HttpRequestMessage requestMessage = new(HttpMethod.Get, requestUri);
        AddConfiguredHeaders(requestMessage);

        try
        {
            using CancellationTokenSource timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(GetTimeout());

            using HttpResponseMessage response = await httpClient.SendAsync(
                requestMessage,
                timeoutCancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
                throw new LocationProviderUnavailableException(
                    $"Nominatim search returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
            }

            await using Stream responseStream =
                await response.Content.ReadAsStreamAsync(timeoutCancellationTokenSource.Token);

            List<NominatimPlace>? places = await JsonSerializer.DeserializeAsync<List<NominatimPlace>>(
                responseStream,
                JsonSerializerOptions,
                timeoutCancellationTokenSource.Token);

            List<GeocodeResultDto> results = (places ?? [])
                .Select(MapToGeocodeResult)
                .Where(result => result != null)
                .Cast<GeocodeResultDto>()
                .ToList();

            return new GeocodeResponse(results);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocationProviderUnavailableException("Nominatim search request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LocationProviderUnavailableException("Nominatim search request failed.", exception);
        }
        catch (JsonException exception)
        {
            throw new LocationProviderUnavailableException("Nominatim search returned an invalid JSON response.", exception);
        }
    }

    /// <summary>
    /// Finds a human-readable location for a coordinate.
    /// </summary>
    /// <param name="request">The reverse geocoding details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The reverse geocoded location when found; otherwise, null.</returns>
    public async Task<ReverseGeocodeResponse?> ReverseGeocodeAsync(
        ReverseGeocodeRequest request,
        CancellationToken cancellationToken = default)
    {
        Uri requestUri = BuildUrl("reverse", new Dictionary<string, string>
        {
            ["format"] = "jsonv2",
            ["lat"] = request.Coordinate.Latitude.ToString("G17", CultureInfo.InvariantCulture),
            ["lon"] = request.Coordinate.Longitude.ToString("G17", CultureInfo.InvariantCulture),
            ["addressdetails"] = "1"
        });

        using HttpRequestMessage requestMessage = new(HttpMethod.Get, requestUri);
        AddConfiguredHeaders(requestMessage);

        try
        {
            using CancellationTokenSource timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCancellationTokenSource.CancelAfter(GetTimeout());

            using HttpResponseMessage response = await httpClient.SendAsync(
                requestMessage,
                timeoutCancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
                throw new LocationProviderUnavailableException(
                    $"Nominatim reverse returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
            }

            await using Stream responseStream =
                await response.Content.ReadAsStreamAsync(timeoutCancellationTokenSource.Token);

            NominatimPlace? place = await JsonSerializer.DeserializeAsync<NominatimPlace>(
                responseStream,
                JsonSerializerOptions,
                timeoutCancellationTokenSource.Token);

            return MapToReverseGeocodeResult(place);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocationProviderUnavailableException("Nominatim reverse request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LocationProviderUnavailableException("Nominatim reverse request failed.", exception);
        }
        catch (JsonException exception)
        {
            throw new LocationProviderUnavailableException("Nominatim reverse returned an invalid JSON response.", exception);
        }
    }

    /// <summary>
    /// Builds a Nominatim request URL with escaped query string values.
    /// </summary>
    /// <param name="path">The Nominatim endpoint path.</param>
    /// <param name="query">The query string values to append.</param>
    /// <returns>The absolute Nominatim request URI.</returns>
    private Uri BuildUrl(string path, IReadOnlyDictionary<string, string> query)
    {
        Uri baseUri = GetBaseUri();
        UriBuilder uriBuilder = new(new Uri(baseUri, path));

        uriBuilder.Query = string.Join(
            "&",
            query.Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));

        return uriBuilder.Uri;
    }

    /// <summary>
    /// Gets the configured Nominatim base URI.
    /// </summary>
    /// <returns>The absolute Nominatim base URI.</returns>
    private Uri GetBaseUri()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            throw new LocationProviderUnavailableException(
                "OpenStreetMap:Nominatim:BaseUrl is not configured.");
        }

        string baseUrl = _options.BaseUrl.TrimEnd('/') + "/";

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new LocationProviderUnavailableException(
                "OpenStreetMap:Nominatim:BaseUrl must be an absolute URL.");
        }

        return baseUri;
    }

    /// <summary>
    /// Gets the effective timeout for outbound Nominatim calls.
    /// </summary>
    /// <returns>The timeout duration.</returns>
    private TimeSpan GetTimeout()
    {
        return TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
    }

    /// <summary>
    /// Gets the effective maximum result count returned by geocoding calls.
    /// </summary>
    /// <returns>The clamped maximum result count.</returns>
    private int GetMaxResults()
    {
        return Math.Clamp(_options.MaxResults, 1, 50);
    }

    /// <summary>
    /// Adds configured user-agent and optional authorization headers.
    /// </summary>
    /// <param name="requestMessage">The outbound HTTP request to update.</param>
    private void AddConfiguredHeaders(HttpRequestMessage requestMessage)
    {
        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            requestMessage.Headers.UserAgent.ParseAdd(_options.UserAgent);
        }

        if (!string.IsNullOrWhiteSpace(_options.AuthorizationHeaderName) &&
            !string.IsNullOrWhiteSpace(_options.AuthorizationHeaderValue))
        {
            requestMessage.Headers.Add(_options.AuthorizationHeaderName, _options.AuthorizationHeaderValue);
        }
    }

    /// <summary>
    /// Maps a Nominatim search result into the API geocoding contract.
    /// </summary>
    /// <param name="place">The Nominatim place to map.</param>
    /// <returns>The geocode result when the place has a usable coordinate; otherwise, null.</returns>
    private static GeocodeResultDto? MapToGeocodeResult(NominatimPlace place)
    {
        GeoCoordinateDto? coordinate = GetCoordinate(place);

        if (coordinate == null || string.IsNullOrWhiteSpace(place.DisplayName))
        {
            return null;
        }

        return new GeocodeResultDto(
            WebUtility.HtmlDecode(place.DisplayName),
            GetCategory(place),
            coordinate,
            GetAddress(place.Address),
            place.Importance);
    }

    /// <summary>
    /// Maps a Nominatim reverse geocoding result into the API contract.
    /// </summary>
    /// <param name="place">The Nominatim place to map.</param>
    /// <returns>The reverse geocoding result when available; otherwise, null.</returns>
    private static ReverseGeocodeResponse? MapToReverseGeocodeResult(NominatimPlace? place)
    {
        if (place == null || !string.IsNullOrWhiteSpace(place.Error))
        {
            return null;
        }

        GeoCoordinateDto? coordinate = GetCoordinate(place);

        if (coordinate == null || string.IsNullOrWhiteSpace(place.DisplayName))
        {
            return null;
        }

        return new ReverseGeocodeResponse(
            WebUtility.HtmlDecode(place.DisplayName),
            GetCategory(place),
            coordinate,
            GetAddress(place.Address));
    }

    /// <summary>
    /// Parses the coordinate values returned by Nominatim.
    /// </summary>
    /// <param name="place">The Nominatim place to inspect.</param>
    /// <returns>The parsed coordinate when valid; otherwise, null.</returns>
    private static GeoCoordinateDto? GetCoordinate(NominatimPlace place)
    {
        if (!double.TryParse(place.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) ||
            !double.TryParse(place.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude))
        {
            return null;
        }

        return new GeoCoordinateDto(latitude, longitude);
    }

    /// <summary>
    /// Gets a human-readable category from Nominatim metadata.
    /// </summary>
    /// <param name="place">The Nominatim place to inspect.</param>
    /// <returns>The formatted category when available; otherwise, null.</returns>
    private static string? GetCategory(NominatimPlace place)
    {
        if (!string.IsNullOrWhiteSpace(place.Type))
        {
            return FormatValue(place.Type);
        }

        return string.IsNullOrWhiteSpace(place.Category)
            ? null
            : FormatValue(place.Category);
    }

    /// <summary>
    /// Builds a compact address from Nominatim's structured address fields.
    /// </summary>
    /// <param name="address">The structured address fields returned by Nominatim.</param>
    /// <returns>The formatted address when available; otherwise, null.</returns>
    private static string? GetAddress(Dictionary<string, string>? address)
    {
        if (address == null || address.Count == 0)
        {
            return null;
        }

        List<string> addressParts = [];

        // Nominatim varies field names by country and OSM object type, so prefer a few common aliases.
        string? road = GetAddressValue(address, "road") ??
                       GetAddressValue(address, "pedestrian") ??
                       GetAddressValue(address, "footway");
        string? houseNumber = GetAddressValue(address, "house_number");
        string? suburb = GetAddressValue(address, "suburb") ??
                         GetAddressValue(address, "neighbourhood");
        string? city = GetAddressValue(address, "city") ??
                       GetAddressValue(address, "town") ??
                       GetAddressValue(address, "village") ??
                       GetAddressValue(address, "municipality");
        string? postcode = GetAddressValue(address, "postcode");
        string? country = GetAddressValue(address, "country");

        if (!string.IsNullOrWhiteSpace(road) && !string.IsNullOrWhiteSpace(houseNumber))
        {
            addressParts.Add($"{road} {houseNumber}");
        }
        else if (!string.IsNullOrWhiteSpace(road))
        {
            addressParts.Add(road);
        }

        foreach (string? addressPart in new[] { suburb, city, postcode, country })
        {
            if (!string.IsNullOrWhiteSpace(addressPart) &&
                !addressParts.Contains(addressPart, StringComparer.OrdinalIgnoreCase))
            {
                addressParts.Add(addressPart);
            }
        }

        return addressParts.Count == 0
            ? null
            : string.Join(", ", addressParts);
    }

    /// <summary>
    /// Gets a decoded structured address value by key.
    /// </summary>
    /// <param name="address">The structured address fields returned by Nominatim.</param>
    /// <param name="key">The address field key to read.</param>
    /// <returns>The decoded address value when present; otherwise, null.</returns>
    private static string? GetAddressValue(Dictionary<string, string> address, string key)
    {
        return address.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? WebUtility.HtmlDecode(value)
            : null;
    }

    /// <summary>
    /// Formats a Nominatim category or type value for display.
    /// </summary>
    /// <param name="value">The raw provider value.</param>
    /// <returns>The formatted display value.</returns>
    private static string FormatValue(string value)
    {
        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(value.Replace('_', ' '));
    }

    /// <summary>
    /// Represents the subset of a Nominatim place response used by the API.
    /// </summary>
    private sealed class NominatimPlace
    {
        /// <summary>
        /// Gets or sets the display name returned by Nominatim.
        /// </summary>
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the latitude returned by Nominatim.
        /// </summary>
        [JsonPropertyName("lat")]
        public string? Latitude { get; set; }

        /// <summary>
        /// Gets or sets the longitude returned by Nominatim.
        /// </summary>
        [JsonPropertyName("lon")]
        public string? Longitude { get; set; }

        /// <summary>
        /// Gets or sets the broad provider category.
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>
        /// Gets or sets the specific provider type.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the provider-specific importance score.
        /// </summary>
        [JsonPropertyName("importance")]
        public double? Importance { get; set; }

        /// <summary>
        /// Gets or sets the structured address fields returned by Nominatim.
        /// </summary>
        [JsonPropertyName("address")]
        public Dictionary<string, string>? Address { get; set; }

        /// <summary>
        /// Gets or sets the error message returned by reverse geocoding when no result is found.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
