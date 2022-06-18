namespace Featurless;

using System.Runtime.CompilerServices;
using Debug = System.Diagnostics.Debug;

internal static unsafe class Tools
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int CountDigits(int value) {
        switch (value) {
            case < 10:   return 1;
            case < 100:  return 2;
            case < 1000: return 3;
            default:
                Debug.Assert(value < 10000, "I did not assumed that your source code had >10000 lines, sorry.");
                return 4;
        }
    }
    /*
        internal static void MemCopy(char* destination, char* source, int size) {
            int block = (sizeof(char) * size) >> 3;
            long* ldest = (long*)destination;
            long* lsrc = (long*)source;
            for (int i = 0; i < block; ++i) {
                *ldest++ = *lsrc++;
            }

            destination = (char*)ldest;
            source = (char*)lsrc;
            size -= (block << 2);

            for (int i = 0; i < size; ++i) {
                *destination++ = *source++;
            }
        }*/

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivBy10(int left) {
        Debug.Assert(left <= 65536, "integer is too big");
        int num = (6554 * left) >> 16; // <=> /10 thanks to the fact that all values < 2^16
        return num;
    }

    internal static void WriteIntegerString(char* destination, int value, int length) {
        destination += length - 1;

        do {
            int nextValue = DivBy10(value);
            int remainder = value - nextValue * 10;
            *destination-- = (char)(remainder + 0x30);
            value = nextValue;
        } while (value != 0);
    }

    internal static void WriteSmallIntegerString(char* dest, int value) {
        int ten = DivBy10(value);
        int unit = value - ten * 10;
        *(int*)dest = 0x30 + ten + ((0x30 + unit) << 0x10);
    }
}
