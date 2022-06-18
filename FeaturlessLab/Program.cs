namespace FeaturlessLab;

public static class Program
{
    private static bool HasFlag(string[]? args, string value) {
        return args == null || args.Length == 0 || args.Contains(value);
    }

    public static void Main(string[] args) {
        if (HasFlag(args, "minitest")) {
            MiniTestLab.Run(Array.Empty<string>());
        }

        if (HasFlag(args, "logger")) {
            LoggerLab.Run();
        }
    }
}
