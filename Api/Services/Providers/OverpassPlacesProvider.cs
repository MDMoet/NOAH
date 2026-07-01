using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Api.Helpers;
using Api.Interfaces.Providers;
using Api.Options;
using Microsoft.Extensions.Options;
using NOAH.Contracts.Common;
using NOAH.Contracts.Locations;

namespace Api.Services.Providers;

/// <summary>
/// Uses a configured Overpass API instance to search live OpenStreetMap places near a coordinate.
/// </summary>
public sealed class OverpassPlacesProvider(
    HttpClient httpClient,
    IOptions<OpenStreetMapModel> openStreetMapOptions)
    : IPlacesProvider
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OverpassOptions _options = openStreetMapOptions.Value.Overpass;

    /// <summary>
    /// Finds nearby OpenStreetMap objects that match the requested search query.
    /// </summary>
    /// <param name="request">The nearby places search details.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The nearby places returned by Overpass.</returns>
    public async Task<NearbyPlacesResponse> GetNearbyPlacesAsync(
        NearbyPlacesRequest request,
        CancellationToken cancellationToken = default)
    {
        Uri interpreterUri = GetInterpreterUri();
        string overpassQuery = BuildOverpassQuery(request, _options);

        using CancellationTokenSource timeoutCancellationTokenSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(GetTimeout());

        using HttpRequestMessage requestMessage = new(HttpMethod.Post, interpreterUri);

        // Overpass accepts its query through the "data" form field.
        requestMessage.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["data"] = overpassQuery
        });

        AddConfiguredHeaders(requestMessage);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                requestMessage,
                timeoutCancellationTokenSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                string responseBody = await response.Content.ReadAsStringAsync(timeoutCancellationTokenSource.Token);
                throw new LocationProviderUnavailableException(
                    $"Overpass returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
            }

            await using Stream responseStream =
                await response.Content.ReadAsStreamAsync(timeoutCancellationTokenSource.Token);

            OverpassResponse? overpassResponse = await JsonSerializer.DeserializeAsync<OverpassResponse>(
                responseStream,
                JsonSerializerOptions,
                timeoutCancellationTokenSource.Token);

            if (overpassResponse == null)
            {
                return new NearbyPlacesResponse([]);
            }

            List<NearbyPlaceDto> nearbyPlaces = overpassResponse.Elements
                .Select(element => MapToNearbyPlace(element, request.Origin))
                .Where(nearbyPlace => nearbyPlace != null)
                .Cast<NearbyPlaceDto>()
                .Where(nearbyPlace => nearbyPlace.DistanceKilometers <= request.RadiusKilometers)
                .OrderBy(nearbyPlace => nearbyPlace.DistanceKilometers)
                .Take(GetMaxResults())
                .ToList();

            return new NearbyPlacesResponse(nearbyPlaces);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocationProviderUnavailableException("Overpass request timed out.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new LocationProviderUnavailableException("Overpass request failed.", exception);
        }
        catch (JsonException exception)
        {
            throw new LocationProviderUnavailableException("Overpass returned an invalid JSON response.", exception);
        }
    }

    /// <summary>
    /// Gets the configured Overpass interpreter endpoint.
    /// </summary>
    /// <returns>The absolute Overpass interpreter URI.</returns>
    private Uri GetInterpreterUri()
    {
        if (string.IsNullOrWhiteSpace(_options.InterpreterUrl))
        {
            throw new LocationProviderUnavailableException(
                "OpenStreetMap:Overpass:InterpreterUrl is not configured.");
        }

        if (!Uri.TryCreate(_options.InterpreterUrl, UriKind.Absolute, out Uri? interpreterUri))
        {
            throw new LocationProviderUnavailableException(
                "OpenStreetMap:Overpass:InterpreterUrl must be an absolute URL.");
        }

        return interpreterUri;
    }

    /// <summary>
    /// Gets the effective timeout for outbound Overpass calls.
    /// </summary>
    /// <returns>The timeout duration.</returns>
    private TimeSpan GetTimeout()
    {
        return TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
    }

    /// <summary>
    /// Gets the effective maximum result count returned by this provider.
    /// </summary>
    /// <returns>The clamped maximum result count.</returns>
    private int GetMaxResults()
    {
        return Math.Clamp(_options.MaxResults, 1, 100);
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
    /// Builds the Overpass QL query for nearby nodes, ways, and relations.
    /// </summary>
    /// <param name="request">The nearby places request.</param>
    /// <param name="options">The Overpass provider options.</param>
    /// <returns>The Overpass QL query text.</returns>
    private static string BuildOverpassQuery(NearbyPlacesRequest request, OverpassOptions options)
    {
        string latitude = request.Origin.Latitude.ToString("G17", CultureInfo.InvariantCulture);
        string longitude = request.Origin.Longitude.ToString("G17", CultureInfo.InvariantCulture);
        int radiusMeters = Convert.ToInt32(Math.Ceiling(request.RadiusKilometers * 1000));
        int timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 1, 180);
        int maxResults = Math.Clamp(options.MaxResults, 1, 100);
        IReadOnlyList<string> tagFilters = ResolveTagFilters(request.Query);

        StringBuilder queryBuilder = new();
        queryBuilder.AppendLine($"[out:json][timeout:{timeoutSeconds}];");
        queryBuilder.AppendLine("(");

        // Query all three OSM element types. Ways/relations return their center point below.
        foreach (string tagFilter in tagFilters)
        {
            queryBuilder.AppendLine($"  node(around:{radiusMeters},{latitude},{longitude}){tagFilter};");
            queryBuilder.AppendLine($"  way(around:{radiusMeters},{latitude},{longitude}){tagFilter};");
            queryBuilder.AppendLine($"  relation(around:{radiusMeters},{latitude},{longitude}){tagFilter};");
        }

        queryBuilder.AppendLine(");");
        queryBuilder.AppendLine($"out tags center {maxResults};");

        return queryBuilder.ToString();
    }

    /// <summary>
    /// Converts a user query into practical OpenStreetMap tag filters.
    /// </summary>
    /// <param name="query">The user's nearby places query.</param>
    /// <returns>The Overpass tag filters to apply.</returns>
    private static IReadOnlyList<string> ResolveTagFilters(string query)
    {
        string normalizedQuery = NormalizeQuery(query);
        List<string> filters = [];

        // Common terms get explicit OSM tags; unknown terms fall back to a case-insensitive name match.
        if (string.IsNullOrWhiteSpace(normalizedQuery) || ContainsAny(normalizedQuery, "general", "nearby places", "places"))
        {
            filters.Add("[\"amenity\"]");
            filters.Add("[\"shop\"]");
            filters.Add("[\"tourism\"]");
            filters.Add("[\"leisure\"]");
        }

        if (ContainsAny(normalizedQuery, "cafe", "coffee"))
        {
            filters.Add("[\"amenity\"=\"cafe\"]");
            filters.Add("[\"shop\"=\"coffee\"]");
        }

        if (ContainsAny(normalizedQuery, "restaurant", "dinner", "lunch", "food", "eat"))
        {
            filters.Add("[\"amenity\"~\"restaurant|fast_food|food_court|cafe\"]");
        }

        if (ContainsAny(normalizedQuery, "bar", "pub", "drink"))
        {
            filters.Add("[\"amenity\"~\"bar|pub\"]");
        }

        if (ContainsAny(normalizedQuery, "supermarket", "grocery", "groceries", "shop", "store"))
        {
            filters.Add("[\"shop\"~\"supermarket|convenience|greengrocer|department_store|mall\"]");
        }

        if (ContainsAny(normalizedQuery, "fuel", "gas", "petrol"))
        {
            filters.Add("[\"amenity\"=\"fuel\"]");
        }

        if (ContainsAny(normalizedQuery, "pharmacy", "chemist"))
        {
            filters.Add("[\"amenity\"=\"pharmacy\"]");
            filters.Add("[\"healthcare\"=\"pharmacy\"]");
        }

        if (ContainsAny(normalizedQuery, "hospital", "doctor", "clinic", "dentist", "health"))
        {
            filters.Add("[\"amenity\"~\"hospital|clinic|doctors|dentist\"]");
            filters.Add("[\"healthcare\"~\"hospital|clinic|doctor|dentist\"]");
        }

        if (ContainsAny(normalizedQuery, "park", "playground"))
        {
            filters.Add("[\"leisure\"~\"park|playground|garden\"]");
        }

        if (ContainsAny(normalizedQuery, "parking"))
        {
            filters.Add("[\"amenity\"=\"parking\"]");
        }

        if (ContainsAny(normalizedQuery, "atm", "bank"))
        {
            filters.Add("[\"amenity\"~\"atm|bank\"]");
        }

        if (ContainsAny(normalizedQuery, "hotel", "hostel", "motel", "guesthouse"))
        {
            filters.Add("[\"tourism\"~\"hotel|hostel|motel|guest_house\"]");
        }

        if (ContainsAny(normalizedQuery, "school", "university", "college"))
        {
            filters.Add("[\"amenity\"~\"school|university|college\"]");
        }

        if (ContainsAny(normalizedQuery, "gym", "fitness", "sport"))
        {
            filters.Add("[\"leisure\"~\"fitness_centre|sports_centre\"]");
        }

        if (ContainsAny(normalizedQuery, "bus", "train", "tram", "station", "transit"))
        {
            filters.Add("[\"public_transport\"]");
            filters.Add("[\"railway\"~\"station|tram_stop|halt\"]");
            filters.Add("[\"highway\"=\"bus_stop\"]");
        }

        if (ContainsAny(normalizedQuery, "toilet", "restroom", "bathroom"))
        {
            filters.Add("[\"amenity\"=\"toilets\"]");
        }

        if (filters.Count == 0)
        {
            string escapedQuery = EscapeOverpassString(Regex.Escape(query.Trim()));
            filters.Add($"[\"name\"~\"{escapedQuery}\",i]");
        }

        return filters
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Maps an Overpass element into a nearby place DTO.
    /// </summary>
    /// <param name="element">The Overpass element to map.</param>
    /// <param name="origin">The origin coordinate used for distance calculation.</param>
    /// <returns>The nearby place DTO when the element has a usable coordinate; otherwise, null.</returns>
    private static NearbyPlaceDto? MapToNearbyPlace(OverpassElement element, GeoCoordinateDto origin)
    {
        GeoCoordinateDto? coordinate = GetCoordinate(element);

        if (coordinate == null)
        {
            return null;
        }

        Dictionary<string, string> tags = element.Tags ?? [];
        string name = GetName(tags, element);
        string? category = GetCategory(tags);
        string? address = GetAddress(tags);
        double distanceKilometers = LocationDistanceCalculator.CalculateKilometers(origin, coordinate);

        return new NearbyPlaceDto(
            name,
            category,
            coordinate,
            address,
            distanceKilometers);
    }

    /// <summary>
    /// Gets the coordinate from an Overpass element.
    /// </summary>
    /// <param name="element">The Overpass element to inspect.</param>
    /// <returns>The element coordinate when available; otherwise, null.</returns>
    private static GeoCoordinateDto? GetCoordinate(OverpassElement element)
    {
        if (element.Latitude.HasValue && element.Longitude.HasValue)
        {
            return new GeoCoordinateDto(element.Latitude.Value, element.Longitude.Value);
        }

        // Ways and relations usually expose a computed center instead of direct lat/lon fields.
        if (element.Center is { Latitude: not null, Longitude: not null })
        {
            return new GeoCoordinateDto(element.Center.Latitude.Value, element.Center.Longitude.Value);
        }

        return null;
    }

    /// <summary>
    /// Gets the best display name for an Overpass element.
    /// </summary>
    /// <param name="tags">The OSM tags attached to the element.</param>
    /// <param name="element">The Overpass element being mapped.</param>
    /// <returns>The element display name.</returns>
    private static string GetName(Dictionary<string, string> tags, OverpassElement element)
    {
        if (tags.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name))
        {
            return WebUtility.HtmlDecode(name);
        }

        return GetCategory(tags) ?? $"{element.Type} {element.Id}";
    }

    /// <summary>
    /// Gets a human-readable category from common OSM classification tags.
    /// </summary>
    /// <param name="tags">The OSM tags attached to the element.</param>
    /// <returns>The formatted category when available; otherwise, null.</returns>
    private static string? GetCategory(Dictionary<string, string> tags)
    {
        foreach (string key in new[]
                 {
                     "amenity",
                     "shop",
                     "tourism",
                     "leisure",
                     "healthcare",
                     "public_transport",
                     "railway",
                     "office",
                     "craft"
                 })
        {
            if (tags.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value))
            {
                return FormatTagValue(value);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a compact address from common OSM address tags.
    /// </summary>
    /// <param name="tags">The OSM tags attached to the element.</param>
    /// <returns>The formatted address when available; otherwise, null.</returns>
    private static string? GetAddress(Dictionary<string, string> tags)
    {
        if (tags.TryGetValue("addr:full", out string? fullAddress) &&
            !string.IsNullOrWhiteSpace(fullAddress))
        {
            return fullAddress;
        }

        List<string> addressParts = [];

        string? street = GetTagValue(tags, "addr:street");
        string? houseNumber = GetTagValue(tags, "addr:housenumber");
        string? city = GetTagValue(tags, "addr:city") ??
                       GetTagValue(tags, "addr:town") ??
                       GetTagValue(tags, "addr:village");
        string? postcode = GetTagValue(tags, "addr:postcode");
        string? country = GetTagValue(tags, "addr:country");

        if (!string.IsNullOrWhiteSpace(street) && !string.IsNullOrWhiteSpace(houseNumber))
        {
            addressParts.Add($"{street} {houseNumber}");
        }
        else if (!string.IsNullOrWhiteSpace(street))
        {
            addressParts.Add(street);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            addressParts.Add(city);
        }

        if (!string.IsNullOrWhiteSpace(postcode))
        {
            addressParts.Add(postcode);
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            addressParts.Add(country);
        }

        return addressParts.Count == 0
            ? null
            : string.Join(", ", addressParts);
    }

    /// <summary>
    /// Gets a non-empty OSM tag value by key.
    /// </summary>
    /// <param name="tags">The OSM tags attached to the element.</param>
    /// <param name="key">The tag key to read.</param>
    /// <returns>The tag value when present; otherwise, null.</returns>
    private static string? GetTagValue(Dictionary<string, string> tags, string key)
    {
        return tags.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>
    /// Formats an OSM tag value for display.
    /// </summary>
    /// <param name="value">The OSM tag value to format.</param>
    /// <returns>The formatted tag value.</returns>
    private static string FormatTagValue(string value)
    {
        TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
        return textInfo.ToTitleCase(value.Replace('_', ' '));
    }

    /// <summary>
    /// Checks whether a value contains any of the provided candidate terms.
    /// </summary>
    /// <param name="value">The value to inspect.</param>
    /// <param name="candidates">The candidate terms to find.</param>
    /// <returns>True when a candidate term is present; otherwise, false.</returns>
    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes a user query for keyword matching.
    /// </summary>
    /// <param name="query">The query to normalize.</param>
    /// <returns>The normalized query.</returns>
    private static string NormalizeQuery(string query)
    {
        return query
            .Trim()
            .ToLowerInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// Escapes a string for safe use inside an Overpass quoted string.
    /// </summary>
    /// <param name="value">The string to escape.</param>
    /// <returns>The escaped string.</returns>
    private static string EscapeOverpassString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    /// <summary>
    /// Represents the subset of the Overpass JSON response used by the API.
    /// </summary>
    private sealed class OverpassResponse
    {
        /// <summary>
        /// Gets or sets the Overpass elements returned by the query.
        /// </summary>
        [JsonPropertyName("elements")]
        public List<OverpassElement> Elements { get; set; } = [];
    }

    /// <summary>
    /// Represents one Overpass node, way, or relation result.
    /// </summary>
    private sealed class OverpassElement
    {
        /// <summary>
        /// Gets or sets the OSM element type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "element";

        /// <summary>
        /// Gets or sets the OSM element id.
        /// </summary>
        [JsonPropertyName("id")]
        public long Id { get; set; }

        /// <summary>
        /// Gets or sets the latitude for node elements.
        /// </summary>
        [JsonPropertyName("lat")]
        public double? Latitude { get; set; }

        /// <summary>
        /// Gets or sets the longitude for node elements.
        /// </summary>
        [JsonPropertyName("lon")]
        public double? Longitude { get; set; }

        /// <summary>
        /// Gets or sets the computed center for way and relation elements.
        /// </summary>
        [JsonPropertyName("center")]
        public OverpassCenter? Center { get; set; }

        /// <summary>
        /// Gets or sets the OSM tags attached to the element.
        /// </summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; }
    }

    /// <summary>
    /// Represents the computed center coordinate for an Overpass way or relation.
    /// </summary>
    private sealed class OverpassCenter
    {
        /// <summary>
        /// Gets or sets the center latitude.
        /// </summary>
        [JsonPropertyName("lat")]
        public double? Latitude { get; set; }

        /// <summary>
        /// Gets or sets the center longitude.
        /// </summary>
        [JsonPropertyName("lon")]
        public double? Longitude { get; set; }
    }
}
