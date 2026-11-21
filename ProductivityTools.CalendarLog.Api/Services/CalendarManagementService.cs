using Microsoft.EntityFrameworkCore;
using ProductivityTools.CalendarLog.Api.Database;
using ProductivityTools.CalendarLog.Api.Database.Entities;
using ProductivityTools.CalendarLog.Api.Dtos;

namespace ProductivityTools.CalendarLog.Api.Services;

public sealed class DuplicateCalendarException : Exception
{
    public DuplicateCalendarException(string account, string calendarId)
        : base($"Calendar '{calendarId}' is already configured for account '{account}'.")
    {
    }
}

public sealed class CalendarManagementService
{
    private readonly CalendarLogContext _db;
    private readonly int _defaultLookbackDays;

    public CalendarManagementService(CalendarLogContext db, IConfiguration configuration)
    {
        _db = db;
        _defaultLookbackDays = Math.Max(0, configuration.GetValue<int?>("Sync:DefaultLookbackDays") ?? 14);
    }

    public async Task<List<CalendarItemDto>> ListAsync(
        string? account = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.CalendarSyncStates.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(account))
        {
            var normalizedAccount = account.Trim();
            query = query.Where(s => s.Account == normalizedAccount);
        }

        var entities = await query
            .OrderBy(s => s.Account)
            .ThenBy(s => s.CalendarName ?? s.CalendarId)
            .ThenBy(s => s.CalendarId)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task<CalendarItemDto?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.CalendarSyncStateId == id, cancellationToken);

        return entity == null ? null : MapToDto(entity);
    }

    public async Task<CalendarItemDto> CreateAsync(
        CreateCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var calendarName = NormalizeOptional(request.CalendarName);
        var type = NormalizeCalendarType(request.Type, allowDefaultWhenEmpty: true);

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account is required.");
        }

        if (string.IsNullOrEmpty(calendarId))
        {
            throw new ArgumentException("CalendarId is required.");
        }

        var exists = await _db.CalendarSyncStates
            .AnyAsync(s => s.Account == account && s.CalendarId == calendarId, cancellationToken);

        if (exists)
        {
            throw new DuplicateCalendarException(account, calendarId);
        }

        var nowUtc = DateTime.UtcNow;
        var entity = new CalendarSyncState
        {
            Account = account,
            CalendarId = calendarId,
            CalendarName = calendarName,
            Type = type,
            IsActive = request.IsActive,
            DefaultLookbackDays = request.DefaultLookbackDays is >= 0 ? request.DefaultLookbackDays.Value : _defaultLookbackDays,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        _db.CalendarSyncStates.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return MapToDto(entity);
    }

    public async Task<CalendarItemDto?> UpdateAsync(
        int id,
        UpdateCalendarRequest request,
        CancellationToken cancellationToken = default)
    {
        var account = request.Account.Trim();
        var calendarId = request.CalendarId.Trim();
        var calendarName = NormalizeOptional(request.CalendarName);

        if (string.IsNullOrEmpty(account))
        {
            throw new ArgumentException("Account is required.");
        }

        if (string.IsNullOrEmpty(calendarId))
        {
            throw new ArgumentException("CalendarId is required.");
        }

        var entity = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.CalendarSyncStateId == id, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        var nextType = request.Type != null
            ? NormalizeCalendarType(request.Type, allowDefaultWhenEmpty: false)
            : entity.Type;

        var duplicate = await _db.CalendarSyncStates
            .AnyAsync(
                s => s.CalendarSyncStateId != id && s.Account == account && s.CalendarId == calendarId,
                cancellationToken);

        if (duplicate)
        {
            throw new DuplicateCalendarException(account, calendarId);
        }

        entity.Account = account;
        entity.CalendarId = calendarId;
        entity.CalendarName = calendarName;
        entity.Type = nextType;
        entity.IsActive = request.IsActive;
        entity.DefaultLookbackDays = request.DefaultLookbackDays >= 0 ? request.DefaultLookbackDays : _defaultLookbackDays;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<CalendarItemDto?> SetActiveAsync(
        int id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.CalendarSyncStateId == id, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        entity.IsActive = isActive;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<CalendarItemDto?> SetTypeAsync(
        int id,
        string? rawType,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeCalendarType(rawType, allowDefaultWhenEmpty: false);

        var entity = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.CalendarSyncStateId == id, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        entity.Type = normalizedType;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var entity = await _db.CalendarSyncStates
            .SingleOrDefaultAsync(s => s.CalendarSyncStateId == id, cancellationToken);

        if (entity == null)
        {
            return false;
        }

        _db.CalendarSyncStates.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public static string NormalizeCalendarType(string? value, bool allowDefaultWhenEmpty)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (allowDefaultWhenEmpty)
            {
                return CalendarTypes.DataLog;
            }
            throw new ArgumentException("Type must be either 'DataPoint' or 'DataLog'.");
        }

        if (string.Equals(trimmed, CalendarTypes.DataPoint, StringComparison.OrdinalIgnoreCase))
        {
            return CalendarTypes.DataPoint;
        }

        if (string.Equals(trimmed, CalendarTypes.DataLog, StringComparison.OrdinalIgnoreCase))
        {
            return CalendarTypes.DataLog;
        }

        throw new ArgumentException("Type must be either 'DataPoint' or 'DataLog'.");
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static CalendarItemDto MapToDto(CalendarSyncState s) => new()
    {
        CalendarSyncStateId = s.CalendarSyncStateId,
        Account = s.Account,
        CalendarId = s.CalendarId,
        CalendarName = s.CalendarName,
        Type = string.IsNullOrWhiteSpace(s.Type) ? CalendarTypes.DataLog : s.Type,
        IsActive = s.IsActive,
        DefaultLookbackDays = s.DefaultLookbackDays,
        LastSyncedDate = s.LastSyncedDate,
        LastSyncedAtUtc = s.LastSyncedAtUtc,
        CreatedAtUtc = s.CreatedAtUtc,
        UpdatedAtUtc = s.UpdatedAtUtc
    };
}
