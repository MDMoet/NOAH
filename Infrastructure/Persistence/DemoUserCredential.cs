namespace NOAH.Infrastructure.Persistence;

/// <summary>
/// Stores one demo user account that is allowed to sign in from outside the trusted network.
/// </summary>
public sealed class DemoUserCredential
{
    /// <summary>
    /// Gets or sets the primary key of the demo user.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the unique username used during login.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the friendly name shown back to the client.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base64-encoded password salt.
    /// </summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the base64-encoded password hash.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PBKDF2 iteration count used for the stored password hash.
    /// </summary>
    public int PasswordIterations { get; set; } = 100000;

    /// <summary>
    /// Gets or sets whether the account is still allowed to sign in.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets when the user last signed in, in UTC.
    /// </summary>
    public DateTimeOffset? LastSignedInAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the row was created, in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets when the row was last updated, in UTC.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
}
