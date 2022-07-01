namespace Featurless;

using System.Diagnostics;
using System.Runtime.CompilerServices;

internal static unsafe class Tools
{
    private static readonly char[] _hexDigits = {
            '0', '1', '2', '3', '4', '5'
          , '6', '7', '8', '9', 'a', 'b'
          , 'c', 'd', 'e', 'f',
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountDigits(int value) {
        switch (value) {
            case < 10:   return 1;
            case < 100:  return 2;
            case < 1000: return 3;
            default:
                Debug.Assert(value < 10000, "I did not assumed that your source file had >10000 lines, sorry.");
                return 4;
        }
    }

    internal static void WriteThreadId(char* destination) {
        // starting from the less significant digit and formating backward as
        // hexadecimal until end of integer. (all char digits are assumed to be set
        // to 0 before
        int threadId = Thread.CurrentThread.ManagedThreadId;
        while (threadId > 0) {
            int nextVal = threadId >> 4;
            *destination-- = _hexDigits[threadId - (nextVal << 4)];
            threadId = nextVal;
        }
    }

    internal static void WriteIntegerString(char* destination, int value, int length) {
        destination += length - 1;

        do {
            int nextValue = value / 10;
            int remainder = value - nextValue * 10;
            *destination-- = (char) (remainder + 0x30);
            value = nextValue;
        } while (value != 0);
    }

    internal static void WriteSmallIntegerString(char* dest, uint value) {
        // performs better than lookup table (< memory, same speed)
        uint ten = value / 10U;
        uint unit = value - ten * 10U;
        dest[0] = (char)(0x30U + ten);
        dest[1] = (char)(0x30U + unit);
    }
}
