﻿//===-- MiniTest.cs ---------------------------------------------------------------------------===//
//                                  SIMPLE TESTS LIBRARY
//
// Provide simple testing class. Ease-of-use and fast-compile time are
// the main concerns of the library. If you find a feature in it, it's a bug.
// Please report it, correct it, or do whatever you want with it.
//
// Tolerated features are:
// - parsing arguments
// - displaying a program help printed with -h or --help arguments.
// - grouping tests
// - enabling only some groups of tests
// - disabling only some tests.
// You can't disable and enable at the same time. It would be a uselessely
// powerful feature.
//
// Enabling and disabling, will only work if you parse args.
//
// Usage code example:
// void Main(string[] args)
// {
//     Test.ParseArguments(args);
//
//     tester.check("mygroupname", "this is what my test do", true);
//     tester.require("group0", "this would stop if it was false", true);
//     tester.check("mygroupname", "also supporting functions/lambdas", _ => { return true; });
//     tester.check("Evaluating a global test", _ =>{ return true; });
//
//     tester.print_summary();
//     return tester.status();
// }
namespace Featurless;

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>Simple, fast and lightweight unit testing class.</summary>
public sealed class MiniTest
{
    private enum StatusCode { Ok, PrintHelp, RequireFailed }
    private enum LogLevel { Quiet, Normal, Verbose }
    private enum FilterType { None, Enabled, Disabled }
    private const int _minAuthorizedLineMaxWidth = 10;
    private struct Stats
    {
        public StatusCode Status;
        public int CountTotal;
        public int CountEvaluated;
        public int CountSuccess;

        public Stats() {
            Status = StatusCode.Ok;
            CountTotal = 0;
            CountEvaluated = 0;
            CountSuccess = 0;
        }
    }

    private const string _helpDescription = @"Description:
Run featurless tests and summarize results.

Arguments:
    -h, --help      display this help and exit
    -e, --enable    only enable the list of groups provided after this argument
    -d, --disable   disable the list of groups provided after this argument
    -q, --quiet     do not print test failures
    -v, --verbose   print every tests, even successful ones

Remarks:
the 'enable' and 'disable' options can not be used together. The groups given to 'enable' and 'disable' must be separated by spaces
You can do that: program -e group1 group3 group4
You can do that: program -d group2
You can NOT do that: program -e group1 group3 group4 -d group2
";
    private const string _assertUninitializedFilterValues = "The filter values list should be initialized by now (but are not)";
    private Stats _globalStats;
    private readonly LogLevel _logLevel;
    private readonly FilterType _filterType;
    private readonly string[]? _filterValues;
    private readonly Dictionary<string, Stats> _groupStats;
    private int _lineMaxWidth;
    private readonly Stream _outputStream;

    /// <summary>
    /// Only used in console. Get/Set the color used to print success logs (with '-v').
    /// Default value is Console.ForegroundColor.
    /// </summary>
    public ConsoleColor SuccessColor;

    /// <summary>
    /// Only used in console. Get/Set the color used to print failure logs.
    /// Default value is Console.ForegroundColor.
    /// </summary>
    public ConsoleColor ErrorColor;

    /// <summary>True if all test done until now are successfule, otherwise false.</summary>
    public bool StatusOk {
        get => _globalStats.CountEvaluated == _globalStats.CountSuccess;
    }

    /// <summary>
    /// Get/Set the max line width of the failed tests logs. Default to Console.WindowWidth in
    /// console or 120 if using a stream.
    /// </summary>
    /// <exception cref="ArgumentException">When setting the value, if value is lower than minimum (10)</exception>
    public int MaxWidth {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _lineMaxWidth;
        set {
            if (value < _minAuthorizedLineMaxWidth) {
                throw new ArgumentException("MaxWidth argument is too low (<"
                                           + _minAuthorizedLineMaxWidth
                                           + ')');
            }

            _lineMaxWidth = value;
        }
    }

    /// <summary>Get/Set the encoding of the ouptut stream</summary>
    public Encoding Encoding;

    /// <param name="args">The program arguments</param>
    /// <param name="stream">A writeable stream to write into. If null (default) write in console</param>
    /// <exception cref="ArgumentException">throw an ArgumentException if multiple --enable/--disable
    /// options are passed, or if the stream passed can not write
    /// </exception>
    public MiniTest(string[]? args = null, Stream? stream = null) {
        if (stream == null) {
            _outputStream = Console.OpenStandardOutput();
        } else {
            if (!stream.CanWrite) {
                throw new ArgumentException("stream can not write");
            }

            _outputStream = stream;
        }

        _globalStats = new Stats();
        _logLevel = LogLevel.Normal;
        _filterType = FilterType.None;
        _filterValues = null;
        _groupStats = new Dictionary<string, Stats>(24);
        _lineMaxWidth = Console.WindowWidth;
        Encoding = Encoding.Default;

        SuccessColor = Console.ForegroundColor;
        ErrorColor = Console.ForegroundColor;

        if (args != null) {
            const string errorMessageFilterType = "Filter type set to both enabled '-e' and disabled '-d'";
            for (int i = 0; i < args.Length; ++i) {
                string argument = args[i];
                switch (argument) {
                    case "-h":
                    case "--help":
                        Console.Write(_helpDescription);
                        _globalStats.Status = StatusCode.PrintHelp;
                        return;
                    case "-e":
                    case "--enabled":
                        if (_filterType == FilterType.Disabled) {
                            throw new ArgumentException(errorMessageFilterType);
                        }
                        _filterValues = new string[args.Length - i - 1];
                        _filterType = FilterType.Enabled;
                        break;
                    case "-d":
                    case "--disabled":
                        if (_filterType == FilterType.Enabled) {
                            throw new ArgumentException(errorMessageFilterType);
                        }
                        _filterValues = new string[args.Length - i - 1];
                        _filterType = FilterType.Disabled;
                        break;
                    case "-q":
                    case "--quiet":
                        _logLevel = LogLevel.Quiet;
                        break;
                    case "-v":
                    case "--verbose":
                        _logLevel = LogLevel.Verbose;
                        break;
                    default:
                        if (_filterType == FilterType.None) {
                            throw new ArgumentException("invalid argument " + argument);
                        }
                        Debug.Assert(_filterValues != null, _assertUninitializedFilterValues);
                        _filterValues[i - args.Length + _filterValues.Length] = argument;
                        break;
                }
            }
        }
    }

    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="value">result of the test, false if failed, true if successful</param>
    public void Require(string testName, bool value) {
        bool success = InternalCheck(() => value, testName);
        if (!success) {
            _globalStats.Status = StatusCode.RequireFailed;
        }
    }

    /// <param name="groupName">name of the group of the check</param>
    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="value">result of the test, false if failed, true if successful</param>
    /// <exception cref="ArgumentException">No group registered with the provided group name</exception>
    public void Require(string groupName, string testName, bool value) {
        bool success = InternalCheck(() => value, testName, groupName);
        if (!success) {
            ref Stats stats = ref CollectionsMarshal.GetValueRefOrNullRef(_groupStats, groupName);
            Debug.Assert(!Unsafe.IsNullRef(ref stats), "group should always exist after internal check");
            stats.Status = StatusCode.RequireFailed;
        }
    }

    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="expression">a function returning a boolean that is evaluated</param>
    /// <remarks>the expression is evaluated only if needed (the instance of MiniTest is not in failed State</remarks>
    public void Require(string testName, Func<bool> expression) {
        bool success = InternalCheck(expression, testName);
        if (!success) {
            _globalStats.Status = StatusCode.RequireFailed;
        }
    }

    /// <param name="groupName">name of the group of the check</param>
    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="expression">a function returning a boolean that is evaluated</param>
    /// <remarks>the expression is evaluated only if needed (the instance of MiniTest is not in failed State</remarks>
    /// <exception cref="ArgumentException">No group registered with the provided group name</exception>
    public void Require(string groupName, string testName, Func<bool> expression) {
        bool success = InternalCheck(expression, testName, groupName);
        if (!success) {
            ref Stats stats = ref CollectionsMarshal.GetValueRefOrNullRef(_groupStats, groupName);
            Debug.Assert(!Unsafe.IsNullRef(ref stats), "group should always exist after internal check");
            stats.Status = StatusCode.RequireFailed;
        }
    }

    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="value">result of the test, false if failed, true if successful</param>
    public void Check(string testName, bool value) {
        InternalCheck(() => value, testName);
    }

    /// <param name="groupName">name of the group of the check</param>
    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="value">result of the test, false if failed, true if successful</param>
    /// <exception cref="ArgumentException">No group registered with the provided group name</exception>
    public void Check(string groupName, string testName, bool value) {
        InternalCheck(() => value, testName, groupName);
    }

    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="expression">a function returning a boolean that is evaluated</param>
    /// <remarks>the expression is evaluated only if needed (the instance of MiniTest is not in failed State</remarks>
    public void Check(string testName, Func<bool> expression) {
        InternalCheck(expression, testName);
    }

    /// <param name="groupName">name of the group of the check</param>
    /// <param name="testName">name displayed in individual failure/success logs</param>
    /// <param name="expression">a function returning a boolean that is evaluated</param>
    /// <remarks>the expression is evaluated only if needed (the instance of MiniTest is not in failed State</remarks>
    /// <exception cref="ArgumentException">No group registered with the provided group name</exception>
    public void Check(string groupName, string testName, Func<bool> expression) {
        InternalCheck(expression, testName, groupName);
    }

    /// <summary>Write a detailed summary of the tests performed, at a global level and for each group</summary>
    public void Summarize() {
        if (_globalStats.Status == StatusCode.PrintHelp) {
            // no tests done, do not print summary
            return;
        }

        if (_globalStats.CountTotal == 0) {
            _outputStream.Write(Encoding.GetBytes("##### TEST GLOBAL SUMMARY: No tests found"));
            return;
        }

        int globalCoverage = 100 * _globalStats.CountEvaluated / _globalStats.CountTotal;
        int skippedTotal = _globalStats.CountTotal - _globalStats.CountEvaluated;

        _outputStream.Write(Encoding.GetBytes($@"#####   TEST GLOBAL SUMMARY
- successes: {_globalStats.CountSuccess}/{_globalStats.CountEvaluated}
- coverage:  {globalCoverage}% ({skippedTotal} checks skipped)
- total:     {_globalStats.CountTotal} checks
###   Groups Summary   ##
"));

        foreach (string groupName in _groupStats.Keys) {
            if (IsGroupFiltered(groupName)) {
                continue;
            }

            ref Stats stats = ref CollectionsMarshal.GetValueRefOrNullRef(_groupStats, groupName);
            Debug.Assert(!Unsafe.IsNullRef(ref stats), "group registered but unitialized");
            if (stats.CountTotal == 0) {
                _outputStream.Write(Encoding.GetBytes($"- [[{groupName}]] no tests found."));
                continue;
            }

            string isOk = stats.Status == StatusCode.Ok ? "OK" : "KO";
            int coveragePercent = stats.CountTotal > 0 ? 100 * stats.CountEvaluated / stats.CountTotal : -1;
            string str = $"- [{groupName}] status: {isOk}, coverage: {coveragePercent}%, {stats.CountSuccess}/{stats.CountEvaluated} successes{Environment.NewLine}";
            _outputStream.Write(Encoding.GetBytes(str));
        }
    }

    /// <summary>Very brief description of results</summary>
    public override string ToString() {
        return $"Test: {_groupStats.Count} groups, {_globalStats.CountSuccess}/{_globalStats.CountEvaluated} success, {_globalStats.CountTotal} total";
    }

    /// <summary>Who cares about private methods doc ?</summary>
    private void WriteMessage(string message, ConsoleColor color)
    {
        ConsoleColor defaultColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        _outputStream.Write(Encoding.GetBytes(message));
        Console.ForegroundColor = defaultColor;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsGroupFiltered(string groupName) {
        switch (_filterType) {
            case FilterType.Disabled:
                Debug.Assert(_filterValues != null, _assertUninitializedFilterValues);
                return _filterValues.Contains(groupName);
            case FilterType.Enabled:
                Debug.Assert(_filterValues != null, _assertUninitializedFilterValues);
                return !_filterValues.Contains(groupName);
            case FilterType.None:
            default:
                return false;
        }
    }

    /// <summary> Register a group in the MiniTest instance</summary>
    /// <param name="name">The name of the group to register</param>
    /// <exception cref="ArgumentException">A group with the same name has already been registered.</exception>
    private void AddGroup(string name) {
        if (_groupStats.ContainsKey(name)) {
            throw new ArgumentException($"Group '{name}' already added to the test");
        }

        if (_logLevel == LogLevel.Verbose) {
            _outputStream.Write(Encoding.GetBytes($"### Registered group '{name}'{Environment.NewLine}"));
        }

        _groupStats.Add(name, new Stats());
    }

    private bool InternalCheck(Func<bool> expression, string message, string? groupName = null) {
        ref Stats groupStats = ref Unsafe.NullRef<Stats>();
        if (groupName != null) {
            groupStats = ref CollectionsMarshal.GetValueRefOrNullRef(_groupStats, groupName);
            if (Unsafe.IsNullRef(ref groupStats)) {
                AddGroup(groupName);
                groupStats = ref CollectionsMarshal.GetValueRefOrNullRef(_groupStats, groupName);
                if (Unsafe.IsNullRef(ref groupStats)) {
                    throw new KeyNotFoundException($"ailed to register he group '{groupName}'");
                }
            }

            if (groupStats.Status != StatusCode.Ok || IsGroupFiltered(groupName)) {
                return true;
            }
        }

        _globalStats.CountTotal += 1;
        if (groupName != null) {
            groupStats.CountTotal += 1;
        }

        if (_globalStats.Status != StatusCode.Ok) {
            return true;
        }

        if (groupName != null) {
            groupStats.CountEvaluated += 1;
        }

        _globalStats.CountEvaluated += 1;
        if (expression()) {
            _globalStats.CountSuccess += 1;
            if (groupName != null) {
                groupStats.CountSuccess += 1;
            }

            if (_logLevel == LogLevel.Verbose) {
                WriteMessage(message.PadRight(_lineMaxWidth - _minAuthorizedLineMaxWidth, '.') + $".Success{Environment.NewLine}"
                           , SuccessColor);
            }

            return true;
        }

        if (_logLevel != LogLevel.Quiet) {
            WriteMessage(message.PadRight(_lineMaxWidth - _minAuthorizedLineMaxWidth, '.')
                    + $".Failed{Environment.NewLine}", ErrorColor);
        }

        return false;
    }
}
