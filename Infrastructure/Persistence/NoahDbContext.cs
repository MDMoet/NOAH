using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NOAH.Domain.Common;
using NOAH.Domain.Entities;

// ReSharper disable once CheckNamespace
namespace NOAH.Infrastructure.Persistence;

public sealed class NoahDbContext(DbContextOptions<NoahDbContext> options) : DbContext(options)
{
    private static readonly ValueConverter<double, decimal> DoubleToDecimalValueConverter =
        new(
            value => Convert.ToDecimal(value),
            value => Convert.ToDouble(value));

    private static readonly ValueConverter<double?, decimal?> NullableDoubleToNullableDecimalValueConverter =
        new(
            value => value.HasValue ? Convert.ToDecimal(value.Value) : null,
            value => value.HasValue ? Convert.ToDouble(value.Value) : null);

    public DbSet<AssistantInteraction> AssistantInteractions => Set<AssistantInteraction>();
    public DbSet<AssistantChat> AssistantChats => Set<AssistantChat>();
    public DbSet<AssistantMemoryItem> AssistantMemoryItems => Set<AssistantMemoryItem>();
    public DbSet<AssistantSettings> AssistantSettings => Set<AssistantSettings>();
    public DbSet<MileageEntry> MileageEntries => Set<MileageEntry>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<SavedLocation> SavedLocations => Set<SavedLocation>();
    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAssistantInteraction(modelBuilder.Entity<AssistantInteraction>());
        ConfigureAssistantChat(modelBuilder.Entity<AssistantChat>());
        ConfigureAssistantMemoryItem(modelBuilder.Entity<AssistantMemoryItem>());
        ConfigureAssistantSettings(modelBuilder.Entity<AssistantSettings>());
        ConfigureMileageEntry(modelBuilder.Entity<MileageEntry>());
        ConfigureNote(modelBuilder.Entity<Note>());
        ConfigureReminder(modelBuilder.Entity<Reminder>());
        ConfigureSavedLocation(modelBuilder.Entity<SavedLocation>());
        ConfigureTaskItem(modelBuilder.Entity<TaskItem>());
    }

    private static void ConfigureAssistantInteraction(EntityTypeBuilder<AssistantInteraction> entity)
    {
        entity.ToTable("AssistantInteractions");
        ConfigureTrackedEntity(entity);

        entity.Property(assistantInteraction => assistantInteraction.ChatId)
            .IsRequired(false);

        entity.Property(assistantInteraction => assistantInteraction.UserInput)
            .IsRequired();

        entity.Property(assistantInteraction => assistantInteraction.AssistantResponse)
            .IsRequired(false);

        entity.Property(assistantInteraction => assistantInteraction.RelatedEntityType)
            .HasMaxLength(100);

        entity.Property(assistantInteraction => assistantInteraction.ErrorMessage)
            .HasMaxLength(1000);

        entity.Property(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .IsRequired();

        entity.Property(assistantInteraction => assistantInteraction.CompletedAtUtc)
            .IsRequired(false);

        entity.HasOne(assistantInteraction => assistantInteraction.Chat)
            .WithMany(assistantChat => assistantChat.Interactions)
            .HasForeignKey(assistantInteraction => assistantInteraction.ChatId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasIndex(assistantInteraction => assistantInteraction.RequestedAtUtc)
            .HasDatabaseName("IX_AssistantInteractions_RequestedAtUtc");

        entity.HasIndex(assistantInteraction => new
        {
            assistantInteraction.ChatId,
            assistantInteraction.RequestedAtUtc
        })
            .HasDatabaseName("IX_AssistantInteractions_ChatId_RequestedAtUtc");

        entity.HasIndex(assistantInteraction => new
        {
            assistantInteraction.RelatedEntityType,
            assistantInteraction.RelatedEntityId
        })
            .HasDatabaseName("IX_AssistantInteractions_RelatedEntity");
    }

    private static void ConfigureAssistantChat(EntityTypeBuilder<AssistantChat> entity)
    {
        entity.ToTable("AssistantChats");
        ConfigureTrackedEntity(entity);

        entity.Property(assistantChat => assistantChat.Title)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(assistantChat => assistantChat.Description)
            .HasMaxLength(1000);

        entity.Property(assistantChat => assistantChat.LastMessagePreview)
            .HasMaxLength(300);

        entity.Property(assistantChat => assistantChat.LastMessageAtUtc)
            .IsRequired(false);

        entity.HasIndex(assistantChat => assistantChat.LastMessageAtUtc)
            .HasDatabaseName("IX_AssistantChats_LastMessageAtUtc");
    }

    private static void ConfigureAssistantMemoryItem(EntityTypeBuilder<AssistantMemoryItem> entity)
    {
        entity.ToTable("AssistantMemoryItems");
        ConfigureTrackedEntity(entity);

        entity.Property(assistantMemoryItem => assistantMemoryItem.Title)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(assistantMemoryItem => assistantMemoryItem.Content)
            .IsRequired()
            .HasMaxLength(4000);

        entity.Property(assistantMemoryItem => assistantMemoryItem.Tags)
            .HasMaxLength(500);

        entity.HasOne(assistantMemoryItem => assistantMemoryItem.SourceInteraction)
            .WithMany()
            .HasForeignKey(assistantMemoryItem => assistantMemoryItem.SourceInteractionId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(assistantMemoryItem => assistantMemoryItem.SourceChat)
            .WithMany(assistantChat => assistantChat.MemoryItems)
            .HasForeignKey(assistantMemoryItem => assistantMemoryItem.SourceChatId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasIndex(assistantMemoryItem => new
        {
            assistantMemoryItem.IsPinned,
            assistantMemoryItem.UpdatedAtUtc
        })
            .HasDatabaseName("IX_AssistantMemoryItems_IsPinned_UpdatedAtUtc");
    }

    private static void ConfigureAssistantSettings(EntityTypeBuilder<AssistantSettings> entity)
    {
        entity.ToTable("AssistantSettings");
        ConfigureTrackedEntity(entity);

        entity.Property(assistantSettings => assistantSettings.SpeechCulture)
            .IsRequired()
            .HasMaxLength(20);

        entity.Property(assistantSettings => assistantSettings.ConversationMemoryMessageCount)
            .IsRequired();

        entity.Property(assistantSettings => assistantSettings.LongTermMemoryItemCount)
            .IsRequired();
    }

    private static void ConfigureTaskItem(EntityTypeBuilder<TaskItem> entity)
    {
        entity.ToTable("TaskItems");
        ConfigureTrackedEntity(entity);

        entity.Property(taskItem => taskItem.Title)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(taskItem => taskItem.Description)
            .HasMaxLength(2000);

        entity.Property(taskItem => taskItem.DueAtUtc)
            .IsRequired(false);

        entity.Property(taskItem => taskItem.PlannedFor)
            .HasColumnType("date");

        entity.HasIndex(taskItem => new
        {
            taskItem.Status,
            taskItem.DueAtUtc
        })
            .HasDatabaseName("IX_TaskItems_Status_DueAtUtc");

        entity.HasIndex(taskItem => taskItem.PlannedFor)
            .HasDatabaseName("IX_TaskItems_PlannedFor");
    }

    private static void ConfigureNote(EntityTypeBuilder<Note> entity)
    {
        entity.ToTable("Notes");
        ConfigureTrackedEntity(entity);

        entity.Property(note => note.Title)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(note => note.Content)
            .IsRequired();

        entity.HasOne<AssistantInteraction>()
            .WithMany()
            .HasForeignKey(note => note.SourceInteractionId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasIndex(note => note.CreatedAtUtc)
            .HasDatabaseName("IX_Notes_CreatedAtUtc");
    }

    private static void ConfigureSavedLocation(EntityTypeBuilder<SavedLocation> entity)
    {
        entity.ToTable("SavedLocations");
        ConfigureTrackedEntity(entity);

        entity.Property(savedLocation => savedLocation.Name)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(savedLocation => savedLocation.Address)
            .HasMaxLength(500);

        entity.OwnsOne(savedLocation => savedLocation.Coordinate, coordinate =>
        {
            coordinate.Property(geoCoordinate => geoCoordinate.Latitude)
                .HasColumnName("Latitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.Longitude)
                .HasColumnName("Longitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.AccuracyMeters)
                .HasColumnName("AccuracyMeters")
                .HasColumnType("decimal(10,2)")
                .HasConversion(NullableDoubleToNullableDecimalValueConverter)
                .IsRequired(false);
        });

        entity.Navigation(savedLocation => savedLocation.Coordinate)
            .IsRequired();

        entity.HasIndex(savedLocation => savedLocation.Name)
            .HasDatabaseName("IX_SavedLocations_Name");
    }

    private static void ConfigureReminder(EntityTypeBuilder<Reminder> entity)
    {
        entity.ToTable("Reminders");
        ConfigureTrackedEntity(entity);

        entity.Property(reminder => reminder.Title)
            .IsRequired()
            .HasMaxLength(200);

        entity.Property(reminder => reminder.Message)
            .HasMaxLength(2000);

        entity.Property(reminder => reminder.TriggerAtUtc)
            .IsRequired(false);

        entity.Property(reminder => reminder.TriggerRadiusMeters)
            .HasColumnType("decimal(10,2)")
            .HasConversion(NullableDoubleToNullableDecimalValueConverter)
            .IsRequired(false);

        entity.Property(reminder => reminder.LastTriggeredAtUtc)
            .IsRequired(false);

        entity.HasOne<TaskItem>()
            .WithMany()
            .HasForeignKey(reminder => reminder.TaskItemId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne<Note>()
            .WithMany()
            .HasForeignKey(reminder => reminder.NoteId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne<SavedLocation>()
            .WithMany()
            .HasForeignKey(reminder => reminder.SavedLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.OwnsOne(reminder => reminder.TriggerLocation, coordinate =>
        {
            coordinate.Property(geoCoordinate => geoCoordinate.Latitude)
                .HasColumnName("TriggerLatitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.Longitude)
                .HasColumnName("TriggerLongitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.AccuracyMeters)
                .HasColumnName("TriggerAccuracyMeters")
                .HasColumnType("decimal(10,2)")
                .HasConversion(NullableDoubleToNullableDecimalValueConverter)
                .IsRequired(false);
        });

        entity.Navigation(reminder => reminder.TriggerLocation)
            .IsRequired(false);

        entity.HasIndex(reminder => new
        {
            reminder.Status,
            reminder.TriggerAtUtc
        })
            .HasDatabaseName("IX_Reminders_Status_TriggerAtUtc");
    }

    private static void ConfigureMileageEntry(EntityTypeBuilder<MileageEntry> entity)
    {
        entity.ToTable("MileageEntries");
        ConfigureTrackedEntity(entity);

        entity.Property(mileageEntry => mileageEntry.RecordedAtUtc)
            .IsRequired();

        entity.Property(mileageEntry => mileageEntry.OdometerReadingKm)
            .HasColumnType("decimal(18,2)");

        entity.Property(mileageEntry => mileageEntry.TripDistanceKm)
            .HasColumnType("decimal(18,2)")
            .IsRequired(false);

        entity.Property(mileageEntry => mileageEntry.SourceImagePath)
            .HasMaxLength(1024);

        entity.Property(mileageEntry => mileageEntry.Notes)
            .HasMaxLength(2000);

        entity.OwnsOne(mileageEntry => mileageEntry.Location, coordinate =>
        {
            coordinate.Property(geoCoordinate => geoCoordinate.Latitude)
                .HasColumnName("Latitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.Longitude)
                .HasColumnName("Longitude")
                .HasColumnType("decimal(9,6)")
                .HasConversion(DoubleToDecimalValueConverter);

            coordinate.Property(geoCoordinate => geoCoordinate.AccuracyMeters)
                .HasColumnName("AccuracyMeters")
                .HasColumnType("decimal(10,2)")
                .HasConversion(NullableDoubleToNullableDecimalValueConverter)
                .IsRequired(false);
        });

        entity.Navigation(mileageEntry => mileageEntry.Location)
            .IsRequired(false);

        entity.HasIndex(mileageEntry => mileageEntry.RecordedAtUtc)
            .HasDatabaseName("IX_MileageEntries_RecordedAtUtc");
    }

    private static void ConfigureTrackedEntity<TEntity>(EntityTypeBuilder<TEntity> entity)
        where TEntity : TrackedEntity
    {
        entity.HasKey(trackedEntity => trackedEntity.Id);

        entity.Property(trackedEntity => trackedEntity.CreatedAtUtc)
            .IsRequired();

        entity.Property(trackedEntity => trackedEntity.UpdatedAtUtc)
            .IsRequired(false);
    }
}
