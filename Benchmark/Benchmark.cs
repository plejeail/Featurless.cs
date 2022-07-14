﻿// Instead of running, register batches
// Then run benchmark in random order
// args:
// - select group(s) (do not execute others)
// - change benchmark settings


// print tables (nicely, aligned, nice symbols)
// run Action<State, object[]> in x batches of p iterations
// At start sleep 100-200ms to enable JIT Tier 1+ compilation
// state object that can start measurement (faked, instead measure time until fake start and make diff, etc...)
// GROUPS:
// Bench must be associated to a group
// BATCHS:
// b batches of p iterations.
// before running the actual benchmark, we run an estimating un.
// the estimated time will help determine the execution plan.
// Perform p (nb iters per batch) estimation:
// p = max(maxTime / (t * nbBatch), 30)
// Perform b estimation (number of batches):
// b = targetedTime / estimated time per batch (p*t(p))
//
// if b is less than 30 force b to 30 and warn the user about longer exec time
// if total estimated time > max time
//      throw too long bench
//
// Procedure of measurement
// - run the benchmark function 31 times before anything else (enable tier2 JIT)
//      - it may be interesting to check for the time of running 31 times. if greater than
//        autoBenchDuration => warn the user about possible long time exec ? probably not
// - run the b batches of p iterations
//    - for each batch store its average response time
// - store computation times
/*
BENCH NAME▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁
name   | batches,iterations | Executions/s | Average |   Min   |   Q25%  |   Q50%  |   Q75%  |   Max   | St. Dev.
{name} | 0000000,0000000000 | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔
*/
namespace Featurless;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

/// <summary> Provide options to be used when registering a benchmark in <see cref="Featurless.Benchmarker"/>. </summary>
public readonly struct BenchmarkOptions
{
    /// <summary> The number of measures to be computed by the <see cref="Featurless.Benchmarker"/>. </summary>
    public readonly int MeasuresCount;
    /// <summary> The number of times to call the benchmarked method per measure. </summary>
    public readonly int ItersCountPerMeasure;

    /// <summary> Initialize a new instance of <see cref="Featurless.BenchmarkOptions"/>. </summary>
    /// <param name="measuresCount"> The number of measures to be computed by the <see cref="Featurless.Benchmarker"/>. </param>
    /// <param name="itersCountPerMeasure"> The number of times to call the benchmarked method per measure. </param>
    public BenchmarkOptions(int measuresCount, int itersCountPerMeasure)
    {
        MeasuresCount = measuresCount;
        ItersCountPerMeasure = itersCountPerMeasure;
    }
}

/// <summary> This class run methods benchmarks and display results in summary statistic tables. </summary>
public class Benchmarker
{
#nullable disable
    /// <summary> Plan for playing a registered benchmark. </summary>
    private readonly struct BenchmarkPlannning
    {
        private static int _nextOrderValue/* = 0*/;

        internal readonly string Group;
        private readonly string _name;
        private readonly Action _function;
        private readonly int _measuresCount;
        private readonly int _itersPerMeasure;
        private readonly int _order;

        /// <summary> Create a BenchmarkPlannning instance. </summary>
        /// <param name="group"> The group of the benchmark. </param>
        /// <param name="name"> The name of the benchmark. </param>
        /// <param name="function"> The benchmarked function. </param>
        /// <param name="opts"> The options of the benchmark. </param>
        internal BenchmarkPlannning(string group, string name, Action function, BenchmarkOptions opts) {
            Group = group;
            _name = name;
            _function = function;
            _measuresCount = opts.MeasuresCount;
            _itersPerMeasure = opts.ItersCountPerMeasure;
            _order = _nextOrderValue++;
        }

        /// <summary> Converts this <see cref="Featurless.Benchmarker.BenchmarkPlannning"/> instance to its equivalent
        /// string representation.
        /// </summary>
        /// <returns> The string representation of this instance. </returns>
        public override string ToString() {
            return Group + "::" + _name + " (" + _measuresCount + 'x' + _itersPerMeasure +')';
        }

        /// <summary> Run the benchmark. </summary>
        /// <returns> The benchmark execution statistics. </returns>
        internal Statistics Run() {
            long[] ticks = new long[_measuresCount];
            Stopwatch timer = new();
            for (int i = 0; i < _measuresCount; ++i) {
                timer.Restart();
                for (int j = 0; j < _itersPerMeasure; ++j) {
                    _function();
                }

                timer.Stop();
                ticks[i] = timer.ElapsedTicks / _itersPerMeasure;
            }

            return new Statistics(_name, _order, _itersPerMeasure, ticks);
        }
    }

    /// <summary> Statistics computed from measures computed from a <see cref="BenchmarkPlannning"/>. </summary>
    private readonly struct Statistics
    {
        /// <summary> Name of the benchmark displayed in the results table. </summary>
        internal readonly string Name;
        /// <summary> Ordering value of the Statistics instance. Used to retrieve registering order. </summary>
        internal readonly int Order;
        private readonly int _measuresCount;
        private readonly int _itersPerMeasure;
        private readonly long _average;
        private readonly long _stdev;
        private readonly long _q10;
        private readonly long _q25;
        private readonly long _q50;
        private readonly long _q75;
        private readonly long _q90;

        /// <summary> Create statistics instance. </summary>
        /// <param name="name"> The name of the associated benchmark. </param>
        /// <param name="itersPerMeasure"> The number of iterations done per measure. </param>
        /// <param name="measures"> An array of measured ticks (from stopwatch precision). </param>
        /// <param name="order"> The 'order rank' in the final table.</param>
        internal Statistics(string name, int order, int itersPerMeasure, long[] measures) {
            Name = name;
            Order = order;
            _itersPerMeasure = itersPerMeasure;
            _measuresCount = measures.Length;

            long avg = 0;
            for (int i = 0; i < measures.Length; ++i) {
                avg = avg + measures[i];
            }
            avg = avg / measures.Length;
            _average = avg;

            long stdev = 0;
            for (int i = 0; i < measures.Length; ++i) {
                long value = measures[i] - avg;
                stdev = stdev + value * value;
            }
            stdev = stdev / (measures.Length - 1);
            _stdev = stdev;

            Array.Sort(measures);
            _q10 = measures[measures.Length / 10];
            _q25 = measures[measures.Length / 4];
            _q50 = measures[measures.Length / 2];
            _q75 = measures[3 * measures.Length / 4];
            _q90 = measures[9 * measures.Length / 10];
        }

        /// <summary> Re-compute total benchmarking time. </summary>
        /// <returns> Formatted string of the benchmark running time. </returns>
        internal string GetTotalRunningTime() {
            return FormatTicks(_average * _itersPerMeasure * _measuresCount, 5);
        }

        /// <summary> Add the benchmark formated statistics line to given string builder. </summary>
        /// <param name="sb"> A string builder instance to modify. </param>
        /// <param name="nameLength"> The size of the name field. </param>
        /// <remarks> The name field should have the length of the biggest name of the group + 1. </remarks>
        internal void AppendToString(StringBuilder sb, int nameLength) {
            sb.Append(Name.PadRight(nameLength));
            sb.Append("| ");
            sb.Append(_measuresCount.ToString().PadLeft(7));
            sb.Append(',');
            sb.Append(_itersPerMeasure.ToString().PadRight(11));
            sb.Append("| ");
            double execsPerSec = ExecsPerSecond();
            sb.Append(execsPerSec.ToString("G4").PadLeft(10));
            sb.Append("/s | ");
            sb.Append(FormatTicks(_average, 4));
            sb.Append(" | ");
            sb.Append(FormatTicks(_q10, 4));
            sb.Append(" | ");
            sb.Append(FormatTicks(_q25, 4));
            sb.Append(" | ");
            sb.Append(FormatTicks(_q50, 4));
            sb.Append(" | ");
            sb.Append(FormatTicks(_q75, 4));
            sb.Append(" | ");
            sb.Append(FormatTicks(_q90, 4));
            sb.Append(" | ");
            sb.AppendLine(FormatTicks(_stdev, 4));
        }

        /// <summary> Compute the ExecsPerSecond statistic. </summary>
        /// <returns> The estimated maximum number of possible executions per seconds. </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double ExecsPerSecond() {
            return (double) Stopwatch.Frequency / _average;
        }

        /// <summary> Convert a number of stopwatch ticks to the appropriate time unit. </summary>
        /// <param name="ticks"> The number of ticks to convert. </param>
        /// <param name="length"> the length of the string to return. </param>
        /// <returns> A formated string represented the number of ticks. </returns>
        private string FormatTicks(long ticks, int length) {
            long nanos = (long) ((1_000_000_000D / Stopwatch.Frequency) * ticks);

            if (nanos < 1000L) {
                return nanos.ToString(CultureInfo.InvariantCulture).PadLeft(length + 1) + "ns";
            } else if (nanos < 1_000_000L) {
                double nsd = (double) nanos / 1000L;
                return nsd.ToString("G" + length, CultureInfo.InvariantCulture).PadLeft(length + 1) + "μs";
            } else if (nanos < 1_000_000_000L) {
                double nsd = (double) nanos / 1_000_000L;
                return nsd.ToString("G" + length, CultureInfo.InvariantCulture).PadLeft(length + 1) + "ms";
            } else {
                double nsd = (double) nanos / 1_000_000_000L;
                return nsd.ToString("G" + length, CultureInfo.InvariantCulture).PadLeft(length + 1) + 's';
            }
        }
    }

    private const int _jitTierUpCount = 31;
    private const int _maxMeasuresCount = 1500;
    private const int _minItersCount = 30;

    private readonly long _maxDurationAutoBench;
    private readonly List<BenchmarkPlannning> _plans;
    private readonly Dictionary<string, List<Statistics>> _stats;

    /// <summary> Aimed maximal duration of autoconfigured benchmarks. </summary>
    public TimeSpan MaxDurationAutoConfig {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get { return ToTimeSpan(_maxDurationAutoBench); }
    }

    /// <summary> Initialize a new instance of <see cref="Featurless.Benchmarker"/>. </summary>
    public Benchmarker() : this(TimeSpan.FromSeconds(60)) {}

    /// <summary> Initialize a new instance of <see cref="Featurless.Benchmarker"/>. </summary>
    /// <param name="autoBenchMaxDuration">The aimed duration of autoconfigured benchmarks.</param>
    public Benchmarker(TimeSpan autoBenchMaxDuration) {
        _plans = new List<BenchmarkPlannning>();
        _stats = new Dictionary<string, List<Statistics>>();
        _maxDurationAutoBench = ToStopwatchTicks(autoBenchMaxDuration);

        SetThreadAffinity();
        // tiered compilation trigger only if the JIT did not compiled anything for at least 100ms.
        Thread.Sleep(200);
    }

    /// <summary> Register a <see cref="System.Action"/> benchmark. The benchmark plan is automatically done. </summary>
    /// <param name="group"> The table in which will be displayed the benchmark. </param>
    /// <param name="name"> The displayed name of the benchmark. </param>
    /// <param name="fun"> The delegate to be benchmarked. </param>
    public void Register(string group, string name, Action fun) {
        if (!_stats.ContainsKey(group)) {
            _stats.Add(group, new List<Statistics>());
        }

        // Tiered Jit Preparation
        JitOptimize(fun);
        (int itersCountPerBatch, long batchDuration) = EstimateItersPerMeasureAndDuration(fun);

        int batchesCount = Math.Min((int) (_maxDurationAutoBench / batchDuration), _maxMeasuresCount);
        if (batchesCount < 30) {
            Console.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Failed: Not enough batches ({batchesCount} < 30)");
            return;
        }

        double estimatedTime = batchesCount * MaxDurationAutoConfig.TotalSeconds;
        Console.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} ({batchesCount}x{itersCountPerBatch}, estimated time: {estimatedTime:G4}s)");
        _plans.Add(new BenchmarkPlannning(group, name, fun, new BenchmarkOptions(batchesCount, itersCountPerBatch)));
    }

    /// <summary> Register a <see cref="System.Action"/> benchmark. </summary>
    /// <param name="group"> The table in which will be displayed the benchmark. </param>
    /// <param name="name"> The displayed name of the benchmark. </param>
    /// <param name="fun"> The delegate to be benchmarked. </param>
    /// <param name="opts"> A <see cref="Featurless.BenchmarkOptions"/> instance. </param>
    public void Register(string group, string name, Action fun, BenchmarkOptions opts) {
        if (!_stats.ContainsKey(group)) {
            _stats.Add(group, new List<Statistics>());
        }

        if (opts.MeasuresCount < 30) {
            Console.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Failed: Not enough batches ({opts.MeasuresCount} < 30)");
            return;
        }

        if (opts.ItersCountPerMeasure < 1) {
            Console.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Failed: Not enough iters/batch ({opts.ItersCountPerMeasure} < 1)");
            return;
        }

        JitOptimize(fun);

        Console.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} ({opts.MeasuresCount}x{opts.ItersCountPerMeasure})");
        _plans.Add(new BenchmarkPlannning(group, name, fun, opts));
    }

    /// <summary> Run all the previously registered benchmarks. </summary>
    public void Run() {
        Console.WriteLine("[BENCHMARK::SHUFFLE] Shuffling benchmarks");
        FisherYatesShuffle(_plans);

        // perform benchmarks
        ReadOnlySpan<BenchmarkPlannning> plans = CollectionsMarshal.AsSpan(_plans);
        for (int i = 0; i < plans.Length; ++i) {
            ref readonly BenchmarkPlannning plan = ref plans[i];
            Console.Write("[BENCHMARK::RUN] " + plan + "...");
            Statistics currentStats = plan.Run();
            _stats[plan.Group].Add(currentStats);
            Console.WriteLine("done in " + currentStats.GetTotalRunningTime());
        }

        // reorder results per group in registering order
        foreach (List<Statistics> list in _stats.Values) {
            list.Sort((el1, el2) => el1.Order - el2.Order);
        }
    }

    /// <summary>
    /// Convert this instance of <see cref="Featurless.Benchmarker"/> to a string summary of
    /// the already performed benchmarks.
    /// </summary>
    /// <returns> The string summary. </returns>
    public override string ToString() {
        StringBuilder sb = new(1_000);

        foreach (KeyValuePair<string, List<Statistics>> group in _stats) {
            sb.AppendLine();
            int maxNameSize = 5;
            for (int i = 0; i < group.Value.Count; ++i) {
                maxNameSize = Math.Max(maxNameSize, group.Value[i].Name.Length + 1);
            }

            int linesize = maxNameSize + 106;
            sb.Append("BENCHMARK▁");
            sb.AppendLine(group.Key.ToUpper().Replace(' ', '▁').PadRight(linesize - 10, '▁'));
            sb.Append("Name".PadRight(maxNameSize));
            sb.AppendLine("| batches,iterations | Executions/s | Average |   Q10%  |   Q25%  |   Q50%  |   Q75%  |   Q90%  | St. Dev.");
            for (int i = 0; i < group.Value.Count; ++i) {
                group.Value[i].AppendToString(sb, maxNameSize);
            }

            sb.AppendLine(new string('▔', linesize));
        }

        return sb.ToString();
    }

    /// <summary> Estimate the number of iterations per measure and the duration of a measure. </summary>
    /// <param name="fun"> The benchmarked function. </param>
    /// <returns> A tuple containing the length and the duration. </returns>
    private (int length, long duration) EstimateItersPerMeasureAndDuration(Action fun) {
        Stopwatch measure = Stopwatch.StartNew();
        fun();
        measure.Stop();

        int nbiters = (int) _maxDurationAutoBench / (int) (measure.ElapsedTicks * 30);
        nbiters = Math.Max(nbiters, _minItersCount);
        return (nbiters, nbiters * measure.ElapsedTicks);
    }

    /// <summary> Randomize elements order in a <see cref="System.Collections.Generic.List{T}"/>. </summary>
    /// <param name="list"> The list to randomize. </param>
    /// <typeparam name="T"> The type of elements in the list. </typeparam>
    private static void FisherYatesShuffle<T>(List<T> list) {
        Random rnd = new();
        int count = list.Count;
        int last = count - 1;
        for (int i = 0; i < last; ++i) {
            int r = rnd.Next(i, count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    ///<summary>Convert a <see cref="System.TimeSpan"/> to <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    /// <param name="time">a <see cref="System.TimeSpan"/>.</param>
    /// <returns>the number of ticks of equivalent duration for a <see cref="System.Diagnostics.Stopwatch"/>.</returns>
    private static long ToStopwatchTicks(TimeSpan time) {
        return time.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
    }

    ///<summary>Convert a <see cref="System.Diagnostics.Stopwatch"/> ticks to a <see cref="System.TimeSpan"/>.</summary>
    /// <param name="ticks">The stopwatch number of ticks.</param>
    /// <returns>A <see cref="System.TimeSpan"/> of equivalent duration.</returns>
    private static TimeSpan ToTimeSpan(long ticks) {
        return TimeSpan.FromTicks(ticks * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    }

    /// <summary> Try to induce the JIT-compiler to increase the compilation tier of an <see cref="System.Action"/>. </summary>
    /// <param name="action">The action to upgrade</param>
    private static void JitOptimize(Action action) {
        for (int i = 0; i < _jitTierUpCount; ++i) {
            action();
        }
    }

    /// <summary>
    /// Set this tread affinity to processor 0x0000
    /// </summary>
    private static void SetThreadAffinity() {
        ProcessThreadCollection threads = Process.GetCurrentProcess().Threads;
        for (int i = 0; i < threads.Count; i++) {
            if (OperatingSystem.IsWindows()) {
                threads[i].ProcessorAffinity = (IntPtr) 0x0000;
            }
        }

        if (OperatingSystem.IsLinux()) {
            ulong processorMask = 0x0000UL;
            sched_setaffinity(0, new IntPtr(sizeof(ulong)), ref processorMask);
        }
    }

    // Linux interop ...............................................................................
    [DllImport("libc.so.6", SetLastError = true)]
    private extern static int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong cpuset);
}
