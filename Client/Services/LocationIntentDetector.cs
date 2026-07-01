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
        if (QuestionAboutLocationLogicPattern().IsMatch(normalized))
        {
            return false;
        }

        return LocationIntentPattern().IsMatch(normalized);
    }

    [GeneratedRegex(@"\b(?:would\s+it\s+be|is\s+it|more\s+logical|rather\s+as|logic|design|should\s+it)\b.*\b(?:current\s+location|trigger\s+location|saved\s+location|location[-\s]?based)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestionAboutLocationLogicPattern();

    [GeneratedRegex(@"\b(near me|nearby|current location|current address|my current address|my address|linked to my current location|tied to my current location|connected to my current location|location linked reminder|where am i|where are we|my location|around me|distance to|how far|reverse geocode|geocode this|save my location|save this location|closest|nearest|places nearby|around here)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocationIntentPattern();
}
