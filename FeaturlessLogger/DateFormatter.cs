namespace Featurless;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Fast format locale date.
/// </summary>
internal struct DateFormatter
{
    private long _utcLocaleDiff;
    private long _nextDiffLookupTime;
    private readonly object _updateDiffLock = new ();

    public DateFormatter() {
        DateTime utcNow = DateTime.UtcNow;
        DateTime localeNow = utcNow.ToLocalTime();
        _utcLocaleDiff = (localeNow - utcNow).Ticks;
        _nextDiffLookupTime = localeNow.Date.AddDays(1).Ticks;
    }

    /// <summary>
    /// Write date in provided memory segment. The date is in the following format: YYYY-mm-DD HH:MM:SS.
    /// </summary>
    /// <param name="dest">the destination segment.</param>
    internal unsafe void WriteDate(char* dest) {
        DateTime dt = ComputeDate();
        // byte* dest;
        *(int*)dest = 0x00300032;
        Tools.WriteSmallIntegerString(dest + 2, dt.Year - 31);
        *(dest + 4) = '-';
        Tools.WriteSmallIntegerString(dest + 5, dt.Month);
        *(dest + 7) = '-';
        Tools.WriteSmallIntegerString(dest + 8, dt.Day);
        *(dest + 10) = '\t';
        Tools.WriteSmallIntegerString(dest + 11, dt.Hour);
        *(dest + 13) = ':';
        Tools.WriteSmallIntegerString(dest + 14, dt.Minute);
        *(dest + 16) = ':';
        Tools.WriteSmallIntegerString(dest + 17, dt.Second);
    }

    private DateTime ComputeDate() {
        DateTime dt = new (GetSystemTimeAsTicks() + _utcLocaleDiff, DateTimeKind.Utc);

        if (dt.Ticks > _nextDiffLookupTime) {
            lock (_updateDiffLock) {
                DateTime utcNow = DateTime.UtcNow;
                dt = utcNow.ToLocalTime();
                _utcLocaleDiff = (dt - utcNow).Ticks;
                _nextDiffLookupTime = dt.Date.AddDays(1).Ticks;
            }
        }
        return dt;
    }

    [SuppressGCTransition]
    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetSystemTimeAsTicks")]
    private extern static long GetSystemTimeAsTicks();
}
