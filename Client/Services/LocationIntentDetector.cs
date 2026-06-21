using System.Text.RegularExpressions;

namespace Client.Services;

/// <summary>
/// Detects prompts that likely need the user's current location.
/// </summary>
public static partial class LocationIntentDetector
{
    /// <summary>
    /// Returns true when the current location would probably help fulfill the request.
    /// </summary>
    public static bool RequiresCurrentLocation(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string normalized = input.Trim();
        return LocationIntentPattern().IsMatch(normalized);
    }

    [GeneratedRegex(@"\b(near me|nearby|current location|where am i|where are we|my location|around me|distance to|how far|reverse geocode|geocode this|save my location|save this location|closest|nearest|places nearby|around here)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationIntentPattern();
}
