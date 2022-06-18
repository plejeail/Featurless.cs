﻿//===-- Logger.cs -----------------------------------------------------------------------------===//
//
// Experimenting with memory maps, because why not ?
//
// The Logger class provide an alternative logging tool. This tool include the following features:
// - logging in a file ( in console)
// - file rolling based on file size
//
// And that's all.

// #define FEATURLESS_LOG_LEVEL_DISABLED_TRACE
// #define FEATURLESS_LOG_LEVEL_DISABLED_DEBUG
// #define FEATURLESS_LOG_LEVEL_DISABLED_INFO
// #define FEATURLESS_LOG_LEVEL_DISABLED_WARN
// #define FEATURLESS_LOG_LEVEL_DISABLED_ERROR

namespace Featurless;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using Interlocked = System.Threading.Interlocked;
using MemoryMappedFile = System.IO.MemoryMappedFiles.MemoryMappedFile;
using MemoryMappedViewAccessor = System.IO.MemoryMappedFiles.MemoryMappedViewAccessor;
using MemoryMappedFileAccess = System.IO.MemoryMappedFiles.MemoryMappedFileAccess;
using CallerFilePathAttribute = System.Runtime.CompilerServices.CallerFilePathAttribute;
using CallerLineNumberAttribute = System.Runtime.CompilerServices.CallerLineNumberAttribute;

public sealed unsafe class Logger : IDisposable
{
    #nullable disable
    private enum Level : long
    {
        Trace = 0x0043_0052_0054_0009L, // TRC
        Debug = 0x0047_0042_0044_0009L, // DBG
        Info  = 0x0046_004E_0049_0009L, // INF
        Warn  = 0x004E_0052_0057_0009L, // WRN
        Error = 0x0052_0052_0045_0009L, // ERR
    }

    private const string _extension = ".log";
    private static readonly bool _isWindows =   Environment.OSVersion.Platform == PlatformID.Win32S
                                             || Environment.OSVersion.Platform == PlatformID.Win32Windows
                                             || Environment.OSVersion.Platform == PlatformID.Win32NT
                                             || Environment.OSVersion.Platform == PlatformID.WinCE;
    private long _headOffset;
    private byte* _handlePtr;
    private long _mapBytesLength;
    private int _concurrentWrites;
    private DateFormatter _dateHandler;
    private MemoryMappedViewAccessor _mapView;
    private MemoryMappedFile _mappedFile;
    private int _currentFileIndex;
    private readonly int _maxNumberOfFiles;
    private readonly Queue<int> _logFilePathes;
    private readonly string _logFileBasePath;

    private readonly object _rollingLock = new ();
    // ReSharper disable once InconsistentNaming (maxSizeKB => kilobyte)

    /// <summary>
    /// Create a logger instance.
    /// </summary>
    /// <param name="logFolderPath">folder in which log files are written.</param>
    /// <param name="logNameWithoutExt">name of the log files without extension.</param>
    /// <param name="maxSizeInKB">maximum size of a log file.</param>
    /// <param name="maxNumberOfFiles">maximum number of log files.</param>
    public Logger(string logFolderPath, string logNameWithoutExt, int maxSizeInKB, int maxNumberOfFiles) {
        _dateHandler = new DateFormatter();
        _headOffset = 0L;
        _mapBytesLength = 1000L * (long)maxSizeInKB;
        _maxNumberOfFiles = maxNumberOfFiles;

        _logFileBasePath = Path.Combine(logFolderPath, logNameWithoutExt);
        string[] files = Directory.GetFiles(logFolderPath, logNameWithoutExt  + ".*.log");
        List<int> indices = LookForFileIndices(files);
        _logFilePathes = new Queue<int>(indices);
        _currentFileIndex = indices.Count > 0 ? indices[^1] : 0;
        MapFile(_mapBytesLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Trace(string message
                    , [CallerFilePath] string sourceFile = ""
                    , [CallerLineNumber] int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_TRACE
        WriteLog(message, Level.Trace, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Debug(string message,
                      [CallerFilePath] string sourceFile = "",
                      [CallerLineNumber] int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_DEBUG
        WriteLog(message, Level.Debug, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Info(string message,
                     [CallerFilePath] string sourceFile = "",
                     [CallerLineNumber] int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_INFO
        WriteLog(message, Level.Info, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Warning(string message,
                        [CallerFilePath] string sourceFile = "",
                        [CallerLineNumber] int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_WARN
        WriteLog(message, Level.Warn, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Error(string message
                      , [CallerFilePath] string sourceFile = ""
                      , [CallerLineNumber] int lineNumber = -1) {
#if !FEATURLESS_LOG_LEVEL_DISABLED_ERROR
        WriteLog(message, Level.Error, Path.GetFileName(sourceFile.AsSpan()), lineNumber);
#endif
    }

    /// <summary> Release the memory map.</summary>
    public void Dispose() {
        if (_mappedFile != null) {
            ReleaseMap(_logFileBasePath, _currentFileIndex);
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Write logs in the memory map with format 'YYYY-MM-DD HH:MM:SS LEVEL @(file,line) message'.
    /// </summary>
    /// <param name="message">the message to write.</param>
    /// <param name="level">the level value of the message.</param>
    /// <param name="callerFilePath">caller file path.</param>
    /// <param name="lineNumber">caller line call.</param>
    private void WriteLog(string message, Level level, ReadOnlySpan<char> callerFilePath, int lineNumber) {
        int lineDigitsCount = Tools.CountDigits(lineNumber);
        int callerFilePathLength = callerFilePath.Length;
        int messageLength = message.Length;
        int logSize = EstimatedSize(messageLength + callerFilePathLength + lineDigitsCount);

        // reserve location to write message
        if (_headOffset + logSize * sizeof(char) > _mapBytesLength) {
            RollFile(logSize);
        }
        Interlocked.Increment(ref _concurrentWrites);
        long currentOffset = Interlocked.Add(ref _headOffset, logSize) - logSize;
        char* locationPtr = (char*)(_handlePtr + currentOffset);
        // write date
        _dateHandler.WriteDate(locationPtr);
        *(long*)(locationPtr + 19) = (long)level;
        *(locationPtr + 23) = '\t';
        *(int*)(locationPtr + 24) = 0x00280040;

        // write caller location
        fixed (char* callerFilePathPtr = callerFilePath) {
            //Tools.MemCopy(locationPtr + 26, callerFilePathPtr, callerFilePathLength);
            Unsafe.CopyBlock((void*)(locationPtr + 26), (void*)callerFilePathPtr,
                             (uint) (sizeof(char)*callerFilePathLength));
        }
        locationPtr += callerFilePathLength;
        *(locationPtr + 26) = ',';

        Tools.WriteIntegerString(locationPtr + 27, lineNumber, lineDigitsCount);
        locationPtr += lineDigitsCount;
        *(int*)(locationPtr + 27) = 0x00090029;

        // write message
        fixed (char* messagePtr = message) {
            //Tools.MemCopy(locationPtr + 29, messagePtr, messageLength);
            Unsafe.CopyBlock((void*)(locationPtr + 29), (void*)messagePtr
                           , (uint)(sizeof(char) * messageLength));
        }
        locationPtr += messageLength;

        // write EOL
        if (_isWindows) {
            *(locationPtr + 29) = '\r';
            ++locationPtr;
        }
        *(locationPtr + 29) = '\n';
        Interlocked.Decrement(ref _concurrentWrites);
    }

    /// <summary>
    /// The first thread roll the files while the others wait
    /// </summary>
    private void RollFile(long logSize) {
        lock (_rollingLock) {
            if (_headOffset + logSize * sizeof(char) <= _mapBytesLength) {
                // already rolled
                return;
            }

            long backupMapLength = _mapBytesLength;
            _mapBytesLength = 0L;  // forbid new writes
            WaitForWritesEnd();
            _currentFileIndex += 1;
            MapFile(backupMapLength);
            _headOffset = 0;
            _mapBytesLength = backupMapLength; // reenable writes
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WaitForWritesEnd() {
        SpinWait sw = new ();
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
        List<int> indices = new (logFiles.Length);
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
    private static int EstimatedSize(int dynamicLength) {
        return (Environment.NewLine.Length + 29 + dynamicLength) * sizeof(char);
    }

    private static string GetLogFilePath(string filePath, int index) {
        return $"{filePath}.{index}{_extension}";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void MapFile(long capacity) {
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

        _mappedFile = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, capacity, MemoryMappedFileAccess.ReadWrite);
        _mapView = _mappedFile.CreateViewAccessor(0, capacity);
        _mapView.SafeMemoryMappedViewHandle.AcquirePointer(ref _handlePtr);
    }

    private void ReleaseMap(string filePath, int indexFile) {
        if (_handlePtr != null) {
            _mapView.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        if (_mapView != null) {
            _mapView.Dispose();
            _mapView = null;
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

     ~Logger() {
         ReleaseMap(_logFileBasePath, _currentFileIndex);
     }
}
