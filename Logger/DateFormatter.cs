namespace Featurless;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Fast format locale date.</summary>
internal struct DateFormatter
{
    private static readonly ushort[] _daysToMonth365 = {
            (ushort)0U,   (ushort)31U,  (ushort)59U,  (ushort)90U,  (ushort)120U, (ushort)151U
          , (ushort)181U, (ushort)212U, (ushort)243U, (ushort)273U, (ushort)304U, (ushort)334U
          , (ushort)365U,
    };
    private static readonly ushort[] _daysToMonth366 = {
            (ushort)0U,   (ushort)31U,  (ushort)60U,  (ushort)91U,  (ushort)121U, (ushort)152U
          , (ushort)182U, (ushort)213U, (ushort)244U, (ushort)274U, (ushort)305U, (ushort)335U
          , (ushort)366U,
    };

    private ulong _utcLocaleDiff;
    private ulong _nextDiffLookupTime;
    private readonly object _updateDiffLock;

    public DateFormatter() {
        DateTime utcNow = DateTime.UtcNow;
        DateTime localeNow = utcNow.ToLocalTime();
        _utcLocaleDiff = (ulong) (localeNow - utcNow).Ticks;
        _nextDiffLookupTime = (ulong) localeNow.Date.AddDays(1).Ticks;
        _updateDiffLock = new object();
    }

    /// <summary>Write date in provided memory segment. The date is in the following format: YYYY-mm-DD HH:MM:SS.</summary>
    /// <param name="dest">the destination segment.</param>
    internal unsafe void WriteDateAndTime(char* dest) {
        ulong ticks = ComputeCurrentDate();
        WriteDate(dest, ticks);
        dest[10] = ' ';
        WriteTime(dest + 11, ticks);
    }

    /// <summary>Fast date computation using cached diff bettween locale and utc.</summary>
    /// <remarks>The first time this method is called each day, update the diff.</remarks>
    /// <returns>The number of ticks that represent the value of the current date.</returns>
    private ulong ComputeCurrentDate() {
        ulong ticks;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                                                              || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD)) {
            ticks = (ulong) UnixGetSystemTimeAsTicks() + _utcLocaleDiff;
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            ticks = WindowsGetSystemTimeAsTicks() + _utcLocaleDiff;
        } else {
            throw new NotSupportedException("Unsupported OS");
        }

        if (ticks > _nextDiffLookupTime) {
            lock (_updateDiffLock) {
                DateTime utcNow = DateTime.UtcNow;
                DateTime dt = utcNow.ToLocalTime();
                _utcLocaleDiff = (ulong) (dt - utcNow).Ticks;
                _nextDiffLookupTime = (ulong) dt.Date.AddDays(1).Ticks;
                ticks = (ulong) dt.Ticks;
            }
        }

        return ticks;
    }

    /// <summary>
    ///     Write all date element without repeating call. Adapted from .net6 implementation of DateTime properties: Day, Year, and Month of
    ///     date time
    /// </summary>
    /// <param name="dest">destination segment adress</param>
    /// <param name="ticks">the date as ticksZ</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteDate(char* dest, ulong ticks) {
        uint num1 = (uint) (ticks / 864000000000UL);
        uint num2 = num1 / 146097U;
        uint num3 = num1 - num2 * 146097U;
        uint num4 = num3 / 36524U;
        if (num4 == 4U) {
            num4 = 3U;
        }

        uint num5 = num3 - num4 * 36524U;
        uint num6 = num5 / 1461U;
        uint num7 = num5 - num6 * 1461U;
        uint num8 = num7 / 365U;
        if (num8 == 4U) {
            num8 = 3U;
        }

        // Year
        dest[0] = '2';
        dest[1] = '0';
        Tools.WriteSmallIntegerString(dest + 2, num2 * 400 + num4 * 100 + num6 * 4 + num8 - 30);
        dest[4] = '-';
        ushort num9 = (ushort)(num7 - num8 * 365U);
        ushort[] numArray = num8 != 3U || num6 == 24U && num4 != 3U ? _daysToMonth365 : _daysToMonth366;
        uint index = ((uint)num9 >> 5) + 1U;
        while (num9 >= numArray[index]) {
            ++index;
        }

        // Month
        Tools.WriteSmallIntegerString(dest + 5, index);
        dest[7] = '-';
        // Day of month
        Tools.WriteSmallIntegerString(dest + 8, (uint)num9 - numArray[index - 1] + 1);
    }

    /// <summary>Write all time elements. Adapted from .net6 implementation of DateTime properties: Hour, Minute, Second</summary>
    /// <param name="dest">destination segment adress</param>
    /// <param name="ticks">the date as ticksZ</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe void WriteTime(char* dest, ulong ticks) {
        // Hours
        Tools.WriteSmallIntegerString(dest, (uint) (ticks / 36000000000UL % 24UL));
        dest[2] = ':';
        // Minutes
        Tools.WriteSmallIntegerString(dest + 3, (uint) (ticks / 600000000UL % 60U));
        dest[5] = ':';
        // Seconds
        Tools.WriteSmallIntegerString(dest + 6, (uint) (ticks / 10000000UL % 60UL));
    }

    /// <summary>Interop to get the current date</summary>
    /// <returns>current date as ticks</returns>
    [SuppressGCTransition]
    [DllImport("libSystem.Native", EntryPoint = "SystemNative_GetSystemTimeAsTicks")]
    private extern static long UnixGetSystemTimeAsTicks();

    [StructLayout(LayoutKind.Explicit)]
    private struct WindowsFileTime
    {
#pragma warning disable CS0649 // disable uninitialized warning
        // ReSharper disable FieldCanBeMadeReadOnly.Local
        [FieldOffset(0)]public int dwLowDateTime;
        [FieldOffset(4)]public int dwHighDateTime;
        [FieldOffset(0)]internal ulong ticks;
        // ReSharper restore FieldCanBeMadeReadOnly.Local
#pragma warning restore CS0649
    }

    [DllImport("kernel32")]
    private extern static void GetSystemTimeAsFileTime(ref WindowsFileTime lpSystemTimeAsFileTime);

    // Untested
    private static ulong WindowsGetSystemTimeAsTicks() {
        WindowsFileTime fileTime = new();
        GetSystemTimeAsFileTime(ref fileTime);
        return (ulong) fileTime.ticks;
    }
}
