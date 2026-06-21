namespace Client.Services;

public static class ServiceHelper
{
    public static IServiceProvider Services { get; set; } = null!;

    public static T GetRequiredService<T>()
        where T : notnull
    {
        return (T)(Services.GetService(typeof(T))
            ?? throw new InvalidOperationException($"Required service {typeof(T).Name} is not registered."));
    }
}
