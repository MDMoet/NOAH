using Client.Services;

namespace Client.UnitTests;

public sealed class OdometerRecognitionParserTests
{
    [Fact]
    public void Parse_PrefersLikelyOdometerLineOverTripAndRangeValues()
    {
        OdometerTextObservation[] observations =
        [
            new("Trip 123.4 km", 180, 24, 8, 64),
            new("Range 480 km", 172, 22, 8, 92),
            new("222222 km", 620, 110, 14, 18)
        ];

        OdometerRecognitionResult result = OdometerRecognitionParser.Parse(observations);

        Assert.True(result.IsSuccessful);
        Assert.Equal(222222d, result.OdometerKm);
    }

    [Fact]
    public void Parse_UsesConfusableCharacterNormalization()
    {
        OdometerTextObservation[] observations =
        [
            new("O8S12O km", 540, 96, 20, 30)
        ];

        OdometerRecognitionResult result = OdometerRecognitionParser.Parse(observations);

        Assert.True(result.IsSuccessful);
        Assert.Equal(85120d, result.OdometerKm);
    }
}
