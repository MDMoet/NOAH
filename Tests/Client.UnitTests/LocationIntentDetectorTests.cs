using Client.Services;

namespace Client.UnitTests;

public sealed class LocationIntentDetectorTests
{
    [Theory]
    [InlineData("What is near me right now?")]
    [InlineData("Could you save my location?")]
    [InlineData("How far is Amsterdam from here?")]
    public void RequiresCurrentLocation_ReturnsTrueForLocationDrivenPrompts(string input)
    {
        Assert.True(LocationIntentDetector.RequiresCurrentLocation(input));
    }

    [Theory]
    [InlineData("Why do people note things?")]
    [InlineData("Write me a short story.")]
    [InlineData("")]
    public void RequiresCurrentLocation_ReturnsFalseForNonLocationPrompts(string input)
    {
        Assert.False(LocationIntentDetector.RequiresCurrentLocation(input));
    }
}
