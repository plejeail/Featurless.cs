﻿// ReSharper disable RedundantUsingDirective


namespace FeaturlessLab;


using Featurless.MiniTest;


file static class Program
{
    private static bool HasFlag(string[]? args, string value) {
        return args == null || args.Length == 0 || args.Contains(value);
    }

    public static void Main(string[] args) {
        MiniTest tests = new();
        /* MINITEST *
        if (HasFlag(args, "minitest")) {
            MiniTestLab.Run(Array.Empty<string>());
        }
        /* LOGGER */
        if (Program.HasFlag(args, value: "logger")) {
            LoggerLab.Run();
        }

        /* HASHTABLE *
        if (HasFlag(args, "hashtable")) {
            LinearTableLab.Run(Array.Empty<string>(), tests);
        }
        /* BENCHMARK *
        if (HasFlag(args, "benchmark")) {
            BenchmarkLab.Run();
        }
        /* */
        tests.Summarize();
    }
}
