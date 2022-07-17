namespace FeaturlessLab;

using Featurless;

public static class LoggerLab
{
    private const  int       _fileSize   = 10_000;
    private const  int       _maxFiles   = 10;
    private const  string    _message    = "OK my message is this: 'GET UP Man!'OK my message is this: 'GET UP Man!'";
    private static readonly MapLogger _mapLogger  = new("./", "logger-map", _fileSize, _maxFiles);
    private static readonly Logger _streamLogger = new("./", "logger-stream", _fileSize, _maxFiles);

    private static void LogMmap() {
        _mapLogger.Info(_message);
    }

    private static void LogStream() {
        _streamLogger.Info(_message);
    }

    public static void Run(bool multiThread = true) {
        /* */
        _mapLogger.MinLevel = MapLogger.Level.Debug;
        Benchmarker bench = new();
        bench.Register("Logger", "MMAP ST", LogMmap, new BenchmarkOptions(5000, 200));
        bench.Register("Logger", "MMAP MT", LogMmap, new BenchmarkOptions(5000, 200, true));
        bench.Register("Logger", "STREAM ST", LogStream, new BenchmarkOptions(5000, 200));
        bench.Register("Logger", "STREAM MT", LogStream, new BenchmarkOptions(5000, 200, true));
        bench.Run();

        Console.WriteLine(bench);
        /* */
        _streamLogger.Dispose();
        _mapLogger.Dispose();
    }
}
