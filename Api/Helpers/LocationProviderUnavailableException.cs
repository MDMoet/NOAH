namespace Api.Helpers;

/// <summary>
/// Represents a failure while calling a configured location provider.
/// </summary>
public sealed class LocationProviderUnavailableException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocationProviderUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the provider failure.</param>
    public LocationProviderUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocationProviderUnavailableException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the provider failure.</param>
    /// <param name="innerException">The exception that caused the provider failure.</param>
    public LocationProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
