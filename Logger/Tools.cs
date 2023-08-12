namespace Featurless.Logger;

using System.Runtime.CompilerServices;

internal static unsafe class Tools
{
    private static readonly byte[] _hexDigits = "0123456789abcdef"u8.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountDigits(int value) {
        if (value < 10) {
            return 1;
        }

        if (value < 100) {
            return 2;
        }

        if (value < 1000) {
            return 3;
        }

        if (value < 10000) {
            return 4;
        }
#if Debug
        if (value < 100000) {
            new Exception("I did not assumed that your source file had >100000 lines, sorry.");
        }
#endif
        return 5;
    }

    internal static void WriteThreadId(char* destination) {
        // starting from the less significant digit and formating backward as
        // hexadecimal until end of integer. (all char digits are assumed to be set
        // to 0 before
        int threadId = Thread.CurrentThread.ManagedThreadId;
        do {
            int nextVal = threadId / 16;
            *destination-- = (char)_hexDigits[threadId - nextVal * 16];
            threadId = nextVal;
        } while (threadId > 0);
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
