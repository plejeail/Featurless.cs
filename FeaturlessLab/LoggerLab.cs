namespace FeaturlessLab;


#region

using Featurless.Benchmark;
using Featurless.Logger;

#endregion

public static class LoggerLab
{
    private const int _fileSize = 10_000;
    private const int _maxFiles = 10;
    private const string _message = "OK my message is this: 'GET UP Man!'OK my message is this: 'GET UP Man!'";
    private static readonly MapLogger _mapLogger = new(logFolderPath: "./", logNameWithoutExt: "logger-map",
                                                       LoggerLab._fileSize, LoggerLab._maxFiles);
    private static readonly Logger _streamLogger = new(logFolderPath: "./", logNameWithoutExt: "logger-stream",
                                                       LoggerLab._fileSize, LoggerLab._maxFiles);

    private static void LogMmap() {
        LoggerLab._mapLogger.Info(LoggerLab._message);
    }

    private static void LogStream() {
        LoggerLab._streamLogger.Info(LoggerLab._message);
    }

    public static void Run(bool multiThread = true) {
        /* */
        LoggerLab._mapLogger.MinLevel = MapLogger.Level.Debug;
        Benchmarker bench = new();
        bench.Register(@group: "Logger", name: "MMAP ST", LoggerLab.LogMmap,
                       new BenchmarkOptions(measuresCount: 5000, itersCountPerMeasure: 200));
        bench.Register(@group: "Logger", name: "MMAP MT", LoggerLab.LogMmap,
                       new BenchmarkOptions(measuresCount: 5000, itersCountPerMeasure: 200, multiThread: true));
        bench.Register(@group: "Logger", name: "STREAM ST", LoggerLab.LogStream,
                       new BenchmarkOptions(measuresCount: 5000, itersCountPerMeasure: 200));
        bench.Register(@group: "Logger", name: "STREAM MT", LoggerLab.LogStream,
                       new BenchmarkOptions(measuresCount: 5000, itersCountPerMeasure: 200, multiThread: true));
        bench.Run();
        Console.WriteLine(bench);
        /* */
        LoggerLab._streamLogger.Dispose();
        LoggerLab._mapLogger.Dispose();
    }
}
