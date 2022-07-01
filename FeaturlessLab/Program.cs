// ReSharper disable RedundantUsingDirective
namespace FeaturlessLab;

using Featurless;
using FeaturlessLab.HashTable;

public static class Program
{
    private static bool HasFlag(string[]? args, string value) {
        return args == null || args.Length == 0 || args.Contains(value);
    }

    public static void Main(string[] args) {
        MiniTest tests = new();
        /* *
        if (HasFlag(args, "minitest")) {
            MiniTestLab.Run(Array.Empty<string>());
        }
        /* */
        if (HasFlag(args, "logger")) {
            LoggerLab.Run();
        }
        /* *
        if (HasFlag(args, "hashtable")) {
            LinearTableLab.Run(Array.Empty<string>(), tests);
        }
        /* */
        tests.Summarize();
    }
}
