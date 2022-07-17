//===-- Logger.cs -----------------------------------------------------------------------------===//
//
// Experimenting with memory maps, because why not ?
//
// The Logger class provide an alternative logging tool. This tool include the following features:
// - logging in a file ( in console)
// - file rolling based on file size
//
// Code Example:
// public static void UsageExample() {
//     Featurless.Logger logger = new Featurless.Logger(logFolderPath: "/path/to/my/folder"
//                                                     , logNameWithoutExt: "justMyFilename"
//                                                     , maxSizeInKB: 10000 // 1MB
//                                                     , maxNumberOfFiles: 7);
//      logger.Debug("Ok i'm recorded :)");
//      logger.MinLevel = Featurless.Logger.Level.Warning;
//      logger.Debug("I'm not recorded :(");
//      logger.Error("I'm recorded :p");
// }
// And that's all.

// define constants to disable logs in code
// #define FEATURLESS_LOG_LEVEL_DISABLED_TRACE
// #define FEATURLESS_LOG_LEVEL_DISABLED_DEBUG
// #define FEATURLESS_LOG_LEVEL_DISABLED_INFO
// #define FEATURLESS_LOG_LEVEL_DISABLED_WARN
// #define FEATURLESS_LOG_LEVEL_DISABLED_ERROR


namespace Featurless;

using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;

/// <summary>Simple logger class</summary>
public sealed class MapLogger : IDisposable
{
#nullable disable
    /// <summary>The following levels are defined in order of increasing priority: Debug, Info, Warn, Error, Off.</summary>
    public enum Level
    {
        /// <summary>Diagnosing issues and troubleshooting.</summary>
        Debug,
        /// <summary>Purely informative indications on what happened.</summary>
        Information,
        /// <summary>Unexpected situation, but can continue to work.</summary>
        Warning,
        /// <summary>issue preventing one or more functionalities from properly functioning.</summary>
        Error,
        /// <summary>None</summary>
        Off,
    }

    private const long _levelStringDebug = 0x0047_0042_0044_0020L; // DBG
    private const long _levelStringInfo  = 0x0046_004E_0049_0020L; // INF
    private const long _levelStringWarn  = 0x004E_0052_0057_0020L; // WRN
    private const long _levelStringError = 0x0052_0052_0045_0020L; // ERR
    private const string _extension = ".log";

    private static readonly bool _isWindows = Environment.OSVersion.Platform == PlatformID.Win32S
                                           || Environment.OSVersion.Platform == PlatformID.Win32Windows
                                           || Environment.OSVersion.Platform == PlatformID.Win32NT
                                           || Environment.OSVersion.Platform == PlatformID.WinCE;
    private readonly string _logFileBasePath;
    private readonly Queue<int> _logFilePathes;

    private readonly int _maxNumberOfFiles;
    private readonly object _rollingLock = new();
    private int _concurrentWrites;
    private int _currentFileIndex;
    private DateFormatter _dateHandler;
    private unsafe byte* _handlePtr;
    private long _headOffset;
    private long _mapBytesLength;
    private MemoryMappedFile _mappedFile;
    private MemoryMappedViewAccessor _mapView;

    /// <summary>Log with a lower priority level are not written. Log everything by default.</summary>
    public Level MinLevel = Level.Debug;

    /// <summary>Create a logger instance.</summary>
    /// <param name="logFolderPath">folder in which log files are written.</param>
    /// <param name="logNameWithoutExt">name of the log files without extension.</param>
    /// <param name="maxSizeInKB">maximum size of a log file.</param>
    /// <param name="maxNumberOfFiles">maximum number of log files.</param>
    // ReSharper disable once InconsistentNaming (maxSizeKB => kilobyte)
    public MapLogger(string logFolderPath, string logNameWithoutExt, int maxSizeInKB, int maxNumberOfFiles) {
        _dateHandler = new DateFormatter();
        _headOffset = 0L;
        _mapBytesLength = 1000L * (long) maxSizeInKB;
        _maxNumberOfFiles = maxNumberOfFiles;

        _logFileBasePath = Path.Combine(logFolderPath, logNameWithoutExt + '.');
        string[] files = Directory.GetFiles(logFolderPath, logNameWithoutExt + ".*.log");
        List<int> indices = LookForFileIndices(files);
        _logFilePathes = new Queue<int>(indices);
        _currentFileIndex = indices.Count > 0 ? indices[^1] : 0;
        MapFile(_mapBytesLength);
    }

#pragma warning disable CS1573 // parameters sourceFile and lineNumber should not have a documentation
    /// <summary>Write a debug record in the log file.</summary>
    /// <param name="message">the message of the record</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string message, [CallerFilePath]string sourceFile = "", [CallerLineNumber]int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_DEBUG
        if (MinLevel <= Level.Debug) {
            WriteRecord(message, _levelStringDebug, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
        }
#endif
    }

    /// <summary>Write an information record in the log file.</summary>
    /// <param name="message">the message of the record</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(string message, [CallerFilePath]string sourceFile = "", [CallerLineNumber]int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_INFO
        if (MinLevel <= Level.Information) {
            WriteRecord(message, _levelStringInfo, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
        }
#endif
    }

    /// <summary>Write a warning record in the log file.</summary>
    /// <param name="message">the message of the record</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(string message, [CallerFilePath]string sourceFile = "", [CallerLineNumber]int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_WARN
        if (MinLevel <= Level.Warning) {
            WriteRecord(message, _levelStringWarn, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
        }
#endif
    }

    /// <summary>Write an error record in the log file.</summary>
    /// <param name="message">the message of the record</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message, [CallerFilePath]string sourceFile = "", [CallerLineNumber]int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_ERROR
        if (MinLevel <= Level.Error) {
            WriteRecord(message, _levelStringError, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
        }
#endif
    }
#pragma warning restore CS1573

    /// <summary> Release the memory map.</summary>
    public void Dispose() {
        if (_mappedFile != null) {
            ReleaseMap(_logFileBasePath, _currentFileIndex);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Write logs in the memory map with format:
    /// 'YYYY-MM-DD|HH:MM:SS| LVL |ThreadId|(FileName,Line) Message'.</summary>
    /// <param name="message">the message to write.</param>
    /// <param name="levelString">the level string hexa value.</param>
    /// <param name="callerFilePath">caller file path.</param>
    /// <param name="lineNumber">caller line call.</param>
    private unsafe void WriteRecord(string message,
                                    long levelString,
                                    ReadOnlySpan<char> callerFilePath,
                                    int lineNumber) {
        // size computation
        int lineDigitsCount = Tools.CountDigits(lineNumber);
        int callerFilePathLength = callerFilePath.Length;
        int messageLength = message.Length;
        int recordByteLength = ComputeRecordSize(messageLength + callerFilePathLength + lineDigitsCount);

        // reserve map segment to write
        if (_headOffset + recordByteLength * sizeof(char) > _mapBytesLength) {
            RollFile(recordByteLength);
        }

        Interlocked.Increment(ref _concurrentWrites);
        long currentOffset = Interlocked.Add(ref _headOffset, recordByteLength) - recordByteLength;
        char* locationPtr = (char*) (_handlePtr + currentOffset);

        // date
        _dateHandler.WriteDateAndTime(locationPtr);
        locationPtr[19] = '|';
        *(long*) (locationPtr + 20) = levelString;
        locationPtr[24] = ' ';
        locationPtr[25] = '|';
        // thread Id
        locationPtr[26] = '0';
        locationPtr[27] = 'x';
        locationPtr[28] = '0';
        locationPtr[29] = '0';
        locationPtr[30] = '0';
        locationPtr[31] = '0';
        Tools.WriteThreadId(locationPtr + 31);
        locationPtr[32] = '|';

        locationPtr[33] = '(';
        // caller location
        fixed (char* callerFilePathPtr = callerFilePath) {
            Unsafe.CopyBlockUnaligned((void*) (locationPtr + 34), (void*) callerFilePathPtr
                           , (uint) (sizeof(char) * callerFilePathLength));
        }

        locationPtr += callerFilePathLength;
        locationPtr[34] = ',';

        Tools.WriteIntegerString(locationPtr + 35, lineNumber, lineDigitsCount);
        locationPtr += lineDigitsCount;
        locationPtr[35] = ')';
        locationPtr[36] = ' ';
        locationPtr[37] = ' ';

        // write message
        fixed (char* messagePtr = message) {
            Unsafe.CopyBlockUnaligned((void*) (locationPtr + 38), (void*) messagePtr, (uint) (sizeof(char) * messageLength));
        }

        locationPtr += messageLength;

        // write EOL
        if (_isWindows) {
            locationPtr[38] = '\r';
            ++locationPtr;
        }

        locationPtr[38] = '\n';
        Interlocked.Decrement(ref _concurrentWrites);
    }

    /// <summary>The first thread to enter roll the log file. Following threads wait until rolling end.</summary>
    /// <param name="recordByteLength">size in bytes of the record</param>
    private void RollFile(long recordByteLength) {
        lock (_rollingLock) {
            if (_headOffset + recordByteLength * sizeof(char) <= _mapBytesLength) {
                // already rolled
                return;
            }

            long backupMapLength = _mapBytesLength;
            _mapBytesLength = 0L; // forbid new writes
            WaitForWritesEnd();
            _currentFileIndex += 1;
            MapFile(backupMapLength);
            _headOffset = 0;
            _mapBytesLength = backupMapLength; // reenable writes
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WaitForWritesEnd() {
        SpinWait sw = new();
        do {
            if (_concurrentWrites == 0) {
                break;
            }

            sw.SpinOnce();
        } while (true);
    }

    /// <summary>Extract file indices in an array of log files names</summary>
    /// <param name="logFiles">the list of files. Assumed to contain only log files.</param>
    /// <returns>a list containing the sorted indices.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<int> LookForFileIndices(string[] logFiles) {
        List<int> indices = new(logFiles.Length);
        for (int i = 0; i < logFiles.Length; ++i) {
            string filePath = logFiles[i];
            System.Diagnostics.Debug.Assert(filePath.Length > 4, "too small for a log file");
            int indexEndDot = filePath.Length - _extension.Length;

            int indexStartDot = filePath.LastIndexOf('.', indexEndDot - 1) + 1;
            System.Diagnostics.Debug.Assert(indexStartDot != 0, "logFiles should contains only log files.");
            if (Int32.TryParse(filePath.AsSpan(indexStartDot, indexEndDot - indexStartDot), out int index)) {
                indices.Add(index);
            }
        }

        indices.Sort();
        return indices;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeRecordSize(int dynamicLength) {
        if (_isWindows) { // '\r\n'
            return 80 + dynamicLength * sizeof(char);
        } // '\n'

        return 78 + dynamicLength * sizeof(char);
        // return (Environment.NewLine.Length + 38 + dynamicLength) * sizeof(char);
    }

    private static string GetLogFilePath(string filePath, int index) {
        return filePath + index + _extension;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void MapFile(long capacity) {
        ReleaseMap(_logFileBasePath, _currentFileIndex - 1);

        _logFilePathes.Enqueue(_currentFileIndex);
        if (_logFilePathes.Count > _maxNumberOfFiles) {
            int toDeleteIdx = _logFilePathes.Dequeue();
            string fileToDel = GetLogFilePath(_logFileBasePath, toDeleteIdx);
            if (File.Exists(fileToDel)) {
                File.Delete(fileToDel);
            }
        }

        string filePath = GetLogFilePath(_logFileBasePath, _currentFileIndex);
        if (!File.Exists(filePath)) {
            // should be always the case after first time, but you never know
            File.Create(filePath!).Dispose();
        } else {
            _headOffset = new FileInfo(filePath).Length;
            if (_headOffset >= capacity) {
                // full file go directly to next file
                _headOffset = 0L;
                _currentFileIndex += 1;
                filePath = GetLogFilePath(_logFileBasePath, _currentFileIndex);
                File.Create(filePath!).Dispose();
            }
        }

        _mappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, capacity
                                                    , MemoryMappedFileAccess.ReadWrite);

        _mapView = _mappedFile.CreateViewAccessor(0, capacity);
        _mapView.SafeMemoryMappedViewHandle.AcquirePointer(ref _handlePtr);
    }

    private unsafe void ReleaseMap(string filePath, int indexFile) {
        if (_mapView != null && _handlePtr != null) {
            _mapView.SafeMemoryMappedViewHandle.ReleasePointer();
            _mapView = null;
            _handlePtr = null;
        }

        if (_mappedFile != null) {
            _mappedFile.Dispose();
            _mappedFile = null;

            // truncate log file to the actually written size
            // maybe do it once for all log files in the destructor ?
            FileStream file = File.OpenWrite(GetLogFilePath(filePath, indexFile));
            file.SetLength(_headOffset);
            file.Dispose();
        }
    }

    /// <summary>Release the logger resources, if not already done.</summary>
    ~MapLogger() {
        ReleaseMap(_logFileBasePath, _currentFileIndex);
    }
}

// Faster
/*
    public static ReadOnlySpan<char> GetFileName(ReadOnlySpan<char> path)
    {
      int length1 = Path.GetPathRoot(path).Length;
      int length2 = path.Length;
      while (--length2 >= 0)
      {
        if (length2 < length1 || PathInternal.IsDirectorySeparator(path[length2]))
          return path.Slice(length2 + 1, path.Length - length2 - 1);
      }
      return path;
    }

 */
