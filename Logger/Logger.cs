namespace Featurless;

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

/// <summary>Simple logger class</summary>
public sealed class Logger : IDisposable
{
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

    private const int    _bufferLength     = 65536 / sizeof(char);
    private const long   _levelStringDebug = 0x0047_0042_0044_0020L; // DBG
    private const long   _levelStringInfo  = 0x0046_004E_0049_0020L; // INF
    private const long   _levelStringWarn  = 0x004E_0052_0057_0020L; // WRN
    private const long   _levelStringError = 0x0052_0052_0045_0020L; // ERR
    private const string _extension        = ".log";

    private readonly bool       _osIsUnix;
    private readonly long       _maxFileSize;
    private readonly int        _maxNumberOfFiles;
    private readonly object     _lockFile;
    private unsafe   int*       _fileHandle;
    private readonly string     _logFileBasePath;
    private readonly Queue<int> _logFilePathes;
    private readonly char[]     _buffer;

    private DateFormatter _dateHandler;

    private int    _writtenBufferChars;
    private long   _currentFileSize;
    private int    _currentFileIndex;
    private int    _concurrentWrites;

    /// <summary>Log with a lower priority level are not written. Log everything by default.</summary>
    public Level MinLevel = Level.Debug;

    /// <summary>Create a logger instance.</summary>
    /// <param name="logFolderPath">folder in which log files are written.</param>
    /// <param name="logNameWithoutExt">name of the log files without extension.</param>
    /// <param name="maxSizeInKB">maximum size of a log file.</param>
    /// <param name="maxNumberOfFiles">maximum number of log files.</param>
    // ReSharper disable once InconsistentNaming fileSizeInKB
    public Logger(string logFolderPath, string logNameWithoutExt, int maxSizeInKB, int maxNumberOfFiles) {
        unsafe { _fileHandle = null; }
        _osIsUnix = Environment.OSVersion.Platform == PlatformID.Unix
                 || Environment.OSVersion.Platform == PlatformID.MacOSX;
        _dateHandler = new DateFormatter();
        _concurrentWrites = _writtenBufferChars = 0;
        _currentFileSize = 0L;
        _maxNumberOfFiles = maxNumberOfFiles;
        _maxFileSize = maxSizeInKB * 1000L / sizeof(char);
        _buffer = new char[_bufferLength];
        _lockFile = new object();

        _logFileBasePath = Path.Combine(logFolderPath, logNameWithoutExt + '.');
        string[]  files   = Directory.GetFiles(logFolderPath, logNameWithoutExt + ".*.log");
        List<int> indices = LookForFileIndices(files);
        _logFilePathes = new Queue<int>(indices);
        _currentFileIndex = indices.Count > 0 ? indices[^1] : 0;

        if (_osIsUnix) {
            //_ = SetLocaleUnix(0, "en_US.UTF-8");
        } else {
            if (Environment.OSVersion.Platform != PlatformID.Win32S
                    && Environment.OSVersion.Platform != PlatformID.Win32Windows
                    && Environment.OSVersion.Platform != PlatformID.Win32NT
                    && Environment.OSVersion.Platform != PlatformID.WinCE) {
                throw new NotSupportedException("Your operating system does not seems to be supported.");
            }
            //throw new NotImplementedException();
        }

        OpenFile();
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

    /// <summary>
    /// Write logs in the memory map with format:
    /// 'YYYY-MM-DD|HH:MM:SS| LVL |ThreadId|(FileName,Line) Message'.
    /// </summary>
    /// <param name="message">the message to write.</param>
    /// <param name="levelString">the level string hexa value.</param>
    /// <param name="callerFilePath">caller file path.</param>
    /// <param name="lineNumber">caller line call.</param>
    private unsafe void WriteRecord(string message,
                                    long levelString,
                                    ReadOnlySpan<char> callerFilePath,
                                    int lineNumber) {
        // size computation
        int lineDigitsCount      = Tools.CountDigits(lineNumber);
        int callerFilePathLength = callerFilePath.Length;
        int messageLength        = message.Length;
        int recordByteLength;
        if (_osIsUnix) {
            recordByteLength = ComputeRecordSizeUnix(messageLength + callerFilePathLength + lineDigitsCount);
        } else {
            recordByteLength = ComputeRecordSizeWindows(messageLength + callerFilePathLength + lineDigitsCount);
        }

        char* writeRecordPtr = stackalloc char[recordByteLength];
        char* editRecordPtr = writeRecordPtr;
        // date
        _dateHandler.WriteDateAndTime(editRecordPtr);
        editRecordPtr[19] = '|';
        *(long*) (editRecordPtr + 20) = levelString;
        editRecordPtr[24] = ' ';
        editRecordPtr[25] = '|';
        // thread Id
        editRecordPtr[26] = '0';
        editRecordPtr[27] = 'x';
        editRecordPtr[28] = '0';
        editRecordPtr[29] = '0';
        editRecordPtr[30] = '0';
        editRecordPtr[31] = '0';
        Tools.WriteThreadId(editRecordPtr + 31);
        editRecordPtr[32] = '|';
        editRecordPtr[33] = '(';
        // caller location
        fixed (char* callerFilePathPtr = callerFilePath) {
            Unsafe.CopyBlockUnaligned((void*) (editRecordPtr + 34), (void*) callerFilePathPtr
                           , (uint) (sizeof(char) * callerFilePathLength));
        }
        editRecordPtr += callerFilePathLength;
        editRecordPtr[34] = ',';

        Tools.WriteIntegerString(editRecordPtr + 35, lineNumber, lineDigitsCount);
        editRecordPtr += lineDigitsCount;
        editRecordPtr[35] = ')';
        editRecordPtr[36] = ' ';
        editRecordPtr[37] = ' ';

        // write message
        fixed (char* messagePtr = message) {
            Unsafe.CopyBlockUnaligned((void*) (editRecordPtr + 38), (void*) messagePtr, (uint) (sizeof(char) * messageLength));
        }
        editRecordPtr += messageLength;

        // write EOL
        if (!_osIsUnix) {
            editRecordPtr[38] = '\r';
            ++editRecordPtr;
        }
        editRecordPtr[38] = '\n';


        WriteBuffer(writeRecordPtr, recordByteLength);
    }

    private unsafe void WriteBuffer(char* data, int length) {
        if (length > _bufferLength) {
            lock (_lockFile) {
                WriteFile(data, length);
            }
            _currentFileSize += _bufferLength;
            return;
        }

        long offset = GetBufferOffset(length);
        Interlocked.Increment(ref _concurrentWrites);
        fixed (char* bufPtr = _buffer) {
            Unsafe.CopyBlockUnaligned((void*) (bufPtr + offset), (void*) data, (uint)length * 2);
        }
        Interlocked.Decrement(ref _concurrentWrites);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe int GetBufferOffset(int length) {
        int offset = Interlocked.Add(ref _writtenBufferChars, length) - length;
        while (offset + length > _bufferLength) {
            lock (_lockFile) {
                if (_writtenBufferChars + length > _bufferLength) {
                    WaitForWritesEnd();
                    // rotate
                    if (_currentFileSize + _writtenBufferChars > _maxFileSize) {
                        RollFile();
                    }
                    // write file
                    fixed (char* bufPtr = _buffer) {
                        WriteFile(bufPtr, _writtenBufferChars);
                    }
                    _currentFileSize += _writtenBufferChars;
                    _writtenBufferChars = 0;
                }
            }

            offset = Interlocked.Add(ref _writtenBufferChars, length) - length;
        }
        return offset;
    }

    private void RollFile() {
        CloseFile();
        _currentFileIndex += 1;
        _logFilePathes.Enqueue(_currentFileIndex);
        if (_logFilePathes.Count > _maxNumberOfFiles) {
            int    toDeleteIdx = _logFilePathes.Dequeue();
            string fileToDel   = GetLogFilePath(_logFileBasePath, toDeleteIdx);
            if (File.Exists(fileToDel)) {
                File.Delete(fileToDel);
            }
        }

        OpenFile();
    }

    private unsafe void CloseFile() {
        if (_fileHandle == null) {
            return;
        }

        if (_osIsUnix) {
            CloseFileUnix(_fileHandle);
        } else {
            CloseFileWindows(_fileHandle);
        }

        _fileHandle = null;
    }

    /// <summary>Extract file indices in an array of log files names</summary>
    /// <param name="logFiles">the list of files. Assumed to contain only log files.</param>
    /// <returns>a list containing the sorted indices.</returns>
    private static List<int> LookForFileIndices(string[] logFiles) {
        List<int> indices = new(logFiles.Length);
        for (int i = 0; i < logFiles.Length; ++i) {
            string filePath = logFiles[i];
            //System.Diagnostics.Debug.Assert(filePath.Length > 4, "too small for a log file");
            int indexEndDot = filePath.Length - _extension.Length;

            int indexStartDot = filePath.LastIndexOf('.', indexEndDot - 1) + 1;
            //System.Diagnostics.Debug.Assert(indexStartDot != 0, "logFiles should contains only log files.");
            if (Int32.TryParse(filePath.AsSpan(indexStartDot, indexEndDot - indexStartDot), out int index)) {
                indices.Add(index);
            }
        }

        indices.Sort();
        return indices;
    }

    private unsafe void OpenFile() {
        System.Diagnostics.Debug.Assert(_fileHandle == null, "File must be closed before opening another one.");
        string filename = GetLogFilePath(_logFileBasePath, _currentFileIndex);

        if (_osIsUnix) {
            _fileHandle = OpenFileUnix(filename, "a");
        } else {
            _fileHandle = OpenFileWindows(filename, "a");
        }

        if (_fileHandle == null) {
            throw new IOException("failed to open file " + filename);
        }

        // disable os buffering
        if (_osIsUnix) {
            SetFileBufferUnix(_fileHandle, null);
        } else {
            SetFileBufferWindows(_fileHandle, null);
        }

        _currentFileSize = GetCursorPosition();
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe void WriteFile(char* data, int length) {
        length = 2 * length;
        if (_osIsUnix) {
            WriteFileUnix((byte*) data, length, 1, _fileHandle);
        } else {
            WriteFileWindows((byte*) data, length, 1, _fileHandle);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe long GetCursorPosition() {
        IntPtr cursorPosition;
        if (_osIsUnix) {
            CursorPositionUnix(_fileHandle, out cursorPosition);
        } else {
            CursorPositionWindows(_fileHandle, out cursorPosition);
        }
        return (long)cursorPosition;
    }

    /// <summary> Close the file.</summary>
    public void Dispose() {
        if (_writtenBufferChars > 0) {
            unsafe {
                fixed (char* bufPtr = _buffer) {
                    WriteFile(bufPtr, _writtenBufferChars);
                }
            }
            _writtenBufferChars = 0;
        }

        CloseFile();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeRecordSizeUnix(int dynamicLength) {
        return 39 + dynamicLength;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeRecordSizeWindows(int dynamicLength) {
        return ComputeRecordSizeUnix(dynamicLength) + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetLogFilePath(string filePath, int index) {
        return filePath + index + _extension;
    }

    #region unix interop
    // windows: https://pinvoke.net, CallingConvention = CallingConvention.FastCall
    [DllImport("libc", SetLastError = false, EntryPoint = "fopen")]
    internal extern static unsafe int* OpenFileUnix(string filename, string mode); // use _wfopen_s on windows

    [DllImport("libc", SetLastError = false, EntryPoint = "fclose")]
    internal extern static unsafe int CloseFileUnix(int* handle); // use _wfopen_s on windows

    [DllImport("libc", SetLastError = false, EntryPoint = "setbuf")]
    internal extern static unsafe void SetFileBufferUnix(int* handle, byte* buffer);

    [DllImport("libc", SetLastError = false, EntryPoint = "fwrite")]
    internal extern static unsafe long WriteFileUnix(byte* ptr, long size, long count, int* handle);

    [DllImport("libc", SetLastError = false, EntryPoint = "fgetpos")]
    internal extern static unsafe int CursorPositionUnix(int* stream, out IntPtr pos);

    //[DllImport("libc", EntryPoint = "setlocale")]
    //internal extern static byte* SetLocaleUnix(int category, string locale);
    #endregion

    #region windows interop
    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.FastCall,
               CharSet = CharSet.Ansi, SetLastError = false, EntryPoint = "fopen")]
    internal extern static unsafe int* OpenFileWindows(string filename, string mode);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.FastCall, SetLastError = false, EntryPoint = "fclose")]
    internal extern static unsafe int CloseFileWindows(int* stream);


    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.FastCall,
               SetLastError = false, EntryPoint = "setbuf")]
    internal extern static unsafe void SetFileBufferWindows(int* handle, byte* buffer);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.FastCall, SetLastError = false
             , EntryPoint = "fwrite")]
    internal extern static unsafe long WriteFileWindows(byte* ptr, long size, long count, int* handle);

    [DllImport("msvcrt.dll", CallingConvention = CallingConvention.FastCall, SetLastError = false
             , EntryPoint = "fwrite")]
    internal extern static unsafe int CursorPositionWindows(int* stream, out IntPtr pos);
    #endregion
}
