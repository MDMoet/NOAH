using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NOAH.Infrastructure.Persistence;

/// <summary>
/// Holds the small authentication surface that powers the demo login flow.
/// </summary>
public sealed class DemoAuthenticationDbContext(DbContextOptions<DemoAuthenticationDbContext> options)
    : DbContext(options)
{
    /// <summary>
    /// Gets the externally accessible demo users.
    /// </summary>
    public DbSet<DemoUserCredential> DemoUsers => Set<DemoUserCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureDemoUser(modelBuilder.Entity<DemoUserCredential>());
    }

    private static void ConfigureDemoUser(EntityTypeBuilder<DemoUserCredential> entity)
    {
        entity.ToTable("DemoUsers");

        entity.HasKey(demoUser => demoUser.Id);

        entity.Property(demoUser => demoUser.Username)
            .IsRequired()
            .HasMaxLength(100);

        entity.Property(demoUser => demoUser.DisplayName)
            .IsRequired()
            .HasMaxLength(150);

        entity.Property(demoUser => demoUser.PasswordSalt)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(demoUser => demoUser.PasswordHash)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(demoUser => demoUser.PasswordIterations)
            .IsRequired();

        entity.Property(demoUser => demoUser.IsEnabled)
            .IsRequired();

        entity.Property(demoUser => demoUser.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(demoUser => demoUser.Username)
            .IsUnique()
            .HasDatabaseName("IX_DemoUsers_Username");
    }
}
