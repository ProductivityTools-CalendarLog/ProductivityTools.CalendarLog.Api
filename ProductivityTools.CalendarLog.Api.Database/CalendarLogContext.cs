using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database.Entities;

namespace ProductivityTools.CalendarLog.Api.Database;

public class CalendarLogContext : DbContext
{
    public CalendarLogContext(DbContextOptions<CalendarLogContext> options) : base(options)
    {
    }

    public DbSet<RawCalendarEvent> RawCalendarEvents => Set<RawCalendarEvent>();
    public DbSet<CalendarSyncState> CalendarSyncStates => Set<CalendarSyncState>();
    public DbSet<CalendarConvertRule> CalendarConvertRules => Set<CalendarConvertRule>();
    public DbSet<CalendarTitleRenameRule> CalendarTitleRenameRules => Set<CalendarTitleRenameRule>();
    public DbSet<CalendarValueRenameRule> CalendarValueRenameRules => Set<CalendarValueRenameRule>();
    public DbSet<CalendarCategoryRule> CalendarCategoryRules => Set<CalendarCategoryRule>();
    public DbSet<CalendarCategory> CalendarCategories => Set<CalendarCategory>();
    public DbSet<CalendarLogEntry> CalendarLogs => Set<CalendarLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RawCalendarEvent>(entity =>
        {
            entity.ToTable("RawCalendarEvent", "dbo");
            entity.HasKey(e => e.RawCalendarEventId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ExternalEventId).HasMaxLength(512).IsRequired();
            entity.Property(e => e.ICalUid).HasMaxLength(1024);
            entity.Property(e => e.Title);
            entity.Property(e => e.Location);
            entity.Property(e => e.MyStatus).HasMaxLength(50);
            entity.Property(e => e.EventStatus).HasMaxLength(50);
            entity.Property(e => e.EventType).HasMaxLength(50);
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.ProcessingStatus).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => new { e.Account, e.CalendarId, e.ExternalEventId })
                .IsUnique()
                .HasDatabaseName("UQ_RawCalendarEvent_Account_Calendar_ExternalEvent");

            entity.HasIndex(e => new { e.Account, e.CalendarId, e.StartTime })
                .HasDatabaseName("IX_RawCalendarEvent_Account_Calendar_StartTime");

            entity.HasIndex(e => e.ProcessingStatus)
                .HasDatabaseName("IX_RawCalendarEvent_ProcessingStatus");
        });

        modelBuilder.Entity<CalendarSyncState>(entity =>
        {
            entity.ToTable("CalendarSyncState", "dbo");
            entity.HasKey(e => e.CalendarSyncStateId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarName).HasMaxLength(255);
            entity.Property(e => e.Type).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => new { e.Account, e.CalendarId })
                .IsUnique()
                .HasDatabaseName("UQ_CalendarSyncState_Account_Calendar");
        });

        modelBuilder.Entity<CalendarConvertRule>(entity =>
        {
            entity.ToTable("CalendarConvertRule", "dbo");
            entity.HasKey(e => e.CalendarConvertRuleId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.KeyPrefix).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TargetCalendarId).HasMaxLength(255).IsRequired();

            entity.HasIndex(e => new { e.Account, e.KeyPrefix })
                .IsUnique()
                .HasDatabaseName("UQ_CalendarConvertRule_Account_KeyPrefix");
        });

        modelBuilder.Entity<CalendarTitleRenameRule>(entity =>
        {
            entity.ToTable("CalendarTitleRenameRule", "dbo");
            entity.HasKey(e => e.CalendarTitleRenameRuleId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.KeyPrefix).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TargetPrefix).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.Account, e.CalendarId, e.KeyPrefix })
                .IsUnique()
                .HasDatabaseName("UQ_CalendarTitleRenameRule_Account_Calendar_KeyPrefix");
        });

        modelBuilder.Entity<CalendarValueRenameRule>(entity =>
        {
            entity.ToTable("CalendarValueRenameRule", "dbo");
            entity.HasKey(e => e.CalendarValueRenameRuleId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CategoryPrefix).HasMaxLength(200).IsRequired();
            entity.Property(e => e.KeySuffix).HasMaxLength(200).IsRequired();
            entity.Property(e => e.TargetSuffix).HasMaxLength(200).IsRequired();

            entity.HasIndex(e => new { e.Account, e.CalendarId, e.CategoryPrefix, e.KeySuffix })
                .IsUnique()
                .HasDatabaseName("UQ_CalendarValueRenameRule_Account_Calendar_Category_KeySuffix");
        });

        modelBuilder.Entity<CalendarCategoryRule>(entity =>
        {
            entity.ToTable("CalendarCategoryRule", "dbo");
            entity.HasKey(e => e.CalendarCategoryRuleId);

            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.MatchField).HasMaxLength(50).IsRequired();
            entity.Property(e => e.MatchValue).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(255).IsRequired();

            entity.HasIndex(e => new { e.Account, e.Priority, e.CalendarCategoryRuleId })
                .HasDatabaseName("IX_CalendarCategoryRule_Account_Priority");
        });

        modelBuilder.Entity<CalendarCategory>(entity =>
        {
            entity.ToTable("CalendarCategory", "dbo");
            entity.HasKey(e => e.CalendarCategoryId);

            entity.Property(e => e.Category).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Color).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => e.Category)
                .IsUnique()
                .HasDatabaseName("UQ_CalendarCategory_Category");

            entity.HasIndex(e => new { e.SortOrder, e.CalendarCategoryId })
                .HasDatabaseName("IX_CalendarCategory_SortOrder");
        });

        modelBuilder.Entity<CalendarLogEntry>(entity =>
        {
            entity.ToTable("CalendarLog", "dbo");
            entity.HasKey(e => e.CalendarLogId);

            entity.Property(e => e.Source).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Account).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CalendarId).HasMaxLength(255);
            entity.Property(e => e.CalendarName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Month).HasMaxLength(7).IsRequired();
            entity.Property(e => e.DurationHours).HasPrecision(10, 4);
            entity.Property(e => e.Title);
            entity.Property(e => e.MyStatus).HasMaxLength(50);
            entity.Property(e => e.EventType).HasMaxLength(50);
            entity.Property(e => e.Color).HasMaxLength(50);
            entity.Property(e => e.Category).HasMaxLength(255);
            entity.Property(e => e.Value);

            entity.HasOne(e => e.RawCalendarEvent)
                .WithMany()
                .HasForeignKey(e => e.RawCalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.RawCalendarEventId)
                .IsUnique()
                .HasFilter("[RawCalendarEventId] IS NOT NULL")
                .HasDatabaseName("UQ_CalendarLog_RawCalendarEventId");

            entity.HasIndex(e => new { e.Account, e.Day })
                .HasDatabaseName("IX_CalendarLog_Account_Day");

            entity.HasIndex(e => new { e.Account, e.WeekNumber })
                .HasDatabaseName("IX_CalendarLog_Account_WeekNumber");
        });

        base.OnModelCreating(modelBuilder);
    }
}
