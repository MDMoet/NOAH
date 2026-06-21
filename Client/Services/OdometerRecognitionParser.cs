using System.Globalization;
using System.Text.RegularExpressions;

namespace Client.Services;

/// <summary>
/// Picks the most likely odometer reading from raw OCR observations.
/// </summary>
public static partial class OdometerRecognitionParser
{
    /// <summary>
    /// Parses the OCR observations and returns the best odometer candidate.
    /// </summary>
    public static OdometerRecognitionResult Parse(IEnumerable<OdometerTextObservation> observations)
    {
        List<OdometerTextObservation> lines = observations
            .Where(observation => !string.IsNullOrWhiteSpace(observation.Text))
            .ToList();

        string recognizedText = string.Join(Environment.NewLine, lines.Select(line => line.Text.Trim()));
        List<OdometerRecognitionCandidate> candidates = [];

        if (lines.Count == 0)
        {
            return new OdometerRecognitionResult(false, null, string.Empty, candidates, "No readable text was found.");
        }

        foreach (OdometerTextObservation line in lines)
        {
            foreach (string candidateText in GetCandidateTexts(line.Text))
            {
                foreach (Match match in NumberPattern().Matches(candidateText))
                {
                    double? value = ParseNumericValue(match.Value);

                    if (!value.HasValue)
                    {
                        continue;
                    }

                    double score = ScoreCandidate(match.Value, value.Value, line);

                    if (!string.Equals(candidateText, line.Text, StringComparison.Ordinal))
                    {
                        score += 4;
                    }

                    candidates.Add(new OdometerRecognitionCandidate(
                        match.Value.Trim(),
                        value.Value,
                        score));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return new OdometerRecognitionResult(false, null, recognizedText, candidates, "NOAH could not find a likely odometer value.");
        }

        OdometerRecognitionCandidate bestCandidate = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.ValueKm)
            .First();

        return new OdometerRecognitionResult(
            true,
            bestCandidate.ValueKm,
            recognizedText,
            candidates.OrderByDescending(candidate => candidate.Score).ToList(),
            null);
    }

    private static double ScoreCandidate(
        string rawText,
        double valueKm,
        OdometerTextObservation line)
    {
        string normalizedLine = line.Text.Trim().ToLowerInvariant();
        string digitsOnly = DigitsOnlyPattern().Replace(rawText, string.Empty);

        double score = digitsOnly.Length switch
        {
            >= 6 and <= 7 => 36,
            5 => 30,
            4 => 20,
            8 => 18,
            _ => 8
        };

        if (normalizedLine.Contains("km", StringComparison.Ordinal))
        {
            score += 18;
        }

        if (normalizedLine.Contains("odo", StringComparison.Ordinal) ||
            normalizedLine.Contains("odometer", StringComparison.Ordinal) ||
            normalizedLine.Contains("total", StringComparison.Ordinal))
        {
            score += 12;
        }

        if (normalizedLine.Contains("trip", StringComparison.Ordinal) ||
            normalizedLine.Contains("range", StringComparison.Ordinal) ||
            normalizedLine.Contains("avg", StringComparison.Ordinal) ||
            normalizedLine.Contains("consumption", StringComparison.Ordinal) ||
            normalizedLine.Contains("rpm", StringComparison.Ordinal) ||
            normalizedLine.Contains("temp", StringComparison.Ordinal))
        {
            score -= 12;
        }

        if (valueKm is < 1000 or > 2000000)
        {
            score -= 10;
        }

        score += Math.Min(14, Math.Log10(Math.Max(1, line.Width * line.Height) + 1) * 3.5);

        return score;
    }

    private static IEnumerable<string> GetCandidateTexts(string rawText)
    {
        yield return rawText;

        string normalized = NormalizePotentialDigits(rawText);
        if (!string.Equals(normalized, rawText, StringComparison.Ordinal))
        {
            yield return normalized;
        }
    }

    private static string NormalizePotentialDigits(string rawText)
    {
        char[] characters = rawText.ToCharArray();

        for (int index = 0; index < characters.Length; index++)
        {
            characters[index] = characters[index] switch
            {
                'O' or 'o' or 'Q' or 'D' => '0',
                'I' or 'l' or '|' => '1',
                'Z' => '2',
                'S' or 's' => '5',
                'B' => '8',
                _ => characters[index]
            };
        }

        return new string(characters);
    }

    private static double? ParseNumericValue(string rawText)
    {
        string compact = rawText.Trim().Replace(" ", string.Empty);

        if (GroupedThousandsPattern().IsMatch(compact))
        {
            string flattened = compact.Replace(".", string.Empty).Replace(",", string.Empty);
            if (double.TryParse(flattened, NumberStyles.Number, CultureInfo.InvariantCulture, out double groupedValue))
            {
                return groupedValue;
            }
        }

        if (DecimalPattern().IsMatch(compact))
        {
            string normalized = compact.Replace(',', '.');
            if (double.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out double decimalValue))
            {
                return decimalValue;
            }
        }

        string digitsOnly = DigitsOnlyPattern().Replace(compact, string.Empty);
        return double.TryParse(digitsOnly, NumberStyles.Number, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    [GeneratedRegex(@"(?<!\d)\d[\d., ]{2,}\d(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[^\d]", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnlyPattern();

    [GeneratedRegex(@"^\d{1,3}([.,]\d{3})+$", RegexOptions.CultureInvariant)]
    private static partial Regex GroupedThousandsPattern();

    [GeneratedRegex(@"^\d+[.,]\d{1,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex DecimalPattern();
}
