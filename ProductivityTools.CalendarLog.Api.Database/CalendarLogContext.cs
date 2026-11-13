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
            entity.Property(e => e.ICalUid).HasMaxLength(512);
            entity.Property(e => e.Title).HasMaxLength(1000);
            entity.Property(e => e.Location).HasMaxLength(1000);
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

        base.OnModelCreating(modelBuilder);
    }
}
