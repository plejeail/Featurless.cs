// Instead of running, register batches
// Then run benchmark in random order

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

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

public class Benchmark
{
#nullable disable
    private readonly struct Stats
    {
        private static readonly double _clockRatio = 1_000_000_000D / Stopwatch.Frequency;
        public readonly string Name;
        private readonly int _batchesCount;
        private readonly int _itersPerBatch;
        private readonly long _averageTime;
        private readonly long _standardDeviation;
        private readonly long _q10;
        private readonly long _q25;
        private readonly long _q50;
        private readonly long _q75;
        private readonly long _q90;
        internal Stats(string name, int itersPerBatch, long[] ticks) {
            Name = name;

            _itersPerBatch = itersPerBatch;
            _batchesCount = ticks.Length;

            // average
            _averageTime = 0L;
            for (int i = 0; i < _batchesCount; ++i) {
                _averageTime = _averageTime + ticks[i];
            }
            _averageTime = _averageTime / _batchesCount;

            // standard deviation
            _standardDeviation = 0L;
            for (int i = 0; i < _batchesCount; ++i) {
                long t = (ticks[i] - _averageTime);
                _standardDeviation += t * t;
            }
            _standardDeviation = _standardDeviation / (_batchesCount - 1);
            _standardDeviation = (long)Math.Sqrt(_standardDeviation);

            // quantiles
            Array.Sort(ticks);
            _q10 = ticks[_batchesCount / 10];
            _q25 = ticks[_batchesCount / 4];
            _q50 = ticks[_batchesCount / 2];
            _q75 = ticks[3 * _batchesCount / 4];
            _q90 = ticks[9 * _batchesCount / 10];
        }

        public string ToString(int nameLength) {
            StringBuilder sb = new(nameLength + 94);
            sb.Append(Name.PadRight(nameLength));
            sb.Append("| ");
            sb.Append(_batchesCount.ToString().PadLeft(7));
            sb.Append(',');
            sb.Append(_itersPerBatch.ToString().PadRight(11));
            sb.Append("| ");
            double execsPerSec = ExecsPerSecond();
            sb.Append(execsPerSec.ToString("G4").PadLeft(10));
            sb.Append("/s | ");
            sb.Append(FormatTime(_averageTime, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_q10, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_q25, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_q50, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_q75, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_q90, 4));
            sb.Append(" | ");
            sb.Append(FormatTime(_standardDeviation, 5));
            return sb.ToString();
        }

        private double ExecsPerSecond() {
            return (double)Stopwatch.Frequency / _averageTime;
        }

        private string FormatTime(long ticks, int length) {
            long nanos = (long)(_clockRatio * ticks);

            if (nanos < 1000L) {
                return nanos.ToString(CultureInfo.InvariantCulture).PadLeft(length+1) + "ns";
            } else if (nanos < 1_000_000L) {
                double nsd = (double) nanos / 1000L;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + "μs";
            } else if (nanos < 1_000_000_000L) {
                double nsd = (double) nanos / 1_000_000L;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + "ms";
            } else if (nanos < 1_000_000_000_000L) {
                double nsd = (double) nanos / 1_000_000_000L;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + 's';
            } else { // #E+0
                double nsd = (double) nanos / 1_000_000_000L;
                return nsd.ToString("#.#e0", CultureInfo.InvariantCulture).PadLeft(length + 1) + 's';
            }
        }
    }

    private const int _jitTierUpCount = 31;
    private const int _maxBatchesCount = 5000;
    private const int _maxItersCount = 60;
    private const int _minItersCount = 30;
    private readonly long _maxDurationAutoBatch;
    private readonly Dictionary<string, List<Stats>> _groupValues;

    public Benchmark() : this(TimeSpan.FromSeconds(60))
    {
    }

    public Benchmark(TimeSpan autoBatchMaxDuration) {
        _groupValues = new Dictionary<string, List<Stats>>();
        _maxDurationAutoBatch = HigherPrecision(autoBatchMaxDuration);
        SetThreadAffinity();
        Thread.Sleep(200); // to be sure that tiered jit will be available

        // make itself candidate for tiered JIT
        _groupValues.Add(String.Empty, new List<Stats>());
        for (int i = 0; i < _jitTierUpCount; ++i) {
            InternalRun(String.Empty, String.Empty, () => {}, 2, 1);
        }
        _groupValues.Remove(String.Empty);
    }

    public void Run(string group, string name, Action fun) {
        // Tiered Jit Preparation
        for (int i = 0; i < _jitTierUpCount; ++i) {
            fun();  // Force Tier 1 Jit
        }

        (int itersCountPerBatch, long batchDuration) = EstimateBatchLengthAndDuration(fun);

        int batchesCount = Math.Min((int) (_maxDurationAutoBatch / batchDuration), _maxBatchesCount);
        if (batchesCount < 30) {
            Console.WriteLine($"[Running Benchmark] {group}.{name} Not enough batches ({batchesCount} < 30)");
            return;
        }

        Console.WriteLine($"[Running Benchmark] {group}.{name} ({batchesCount}x{itersCountPerBatch}, estimated time: {batchesCount * LowerPrecision(batchDuration).TotalSeconds:G4}s)");
        InternalRun(group, name, fun, batchesCount, itersCountPerBatch);
    }

    public void Run(string group, string name, Action fun, int batchesCount, int itersCountPerBatch) {
        if (batchesCount < 30) {
            Console.WriteLine($"[Running Benchmark] {group}.{name} Not enough batches ({batchesCount} < 30)");
            return;
        }

        if (itersCountPerBatch < 1) {
            Console.WriteLine($"[Running Benchmark] {group}.{name} Not enough iters/batch ({itersCountPerBatch} < 1)");
            return;
        }

        Console.WriteLine($"[Running Benchmark] {group}.{name} ({batchesCount}x{itersCountPerBatch})");
        // Tiered Jit Preparation
        for (int i = 0; i < _jitTierUpCount; ++i) {
            fun(); // Force Tier 1 Jit
        }
        InternalRun(group, name, fun, batchesCount, itersCountPerBatch);
    }

    public override string ToString() {
        StringBuilder sb = new(1000);
        foreach (KeyValuePair<string, List<Stats>> group in _groupValues) {
            sb.AppendLine();
            int maxNameSize = 5;
            for (int i = 0; i < group.Value.Count; ++i) {
                maxNameSize = Math.Max(maxNameSize, group.Value[i].Name.Length + 1);
            }

            int linesize = maxNameSize + 108;
            sb.Append("BENCHMARK ");
            sb.AppendLine(group.Key.ToUpper().PadRight(linesize - 10, '▁'));
            sb.Append("Name".PadRight(maxNameSize));
            sb.AppendLine("| batches,iterations | Executions/s | Average |   Q10%  |   Q25%  |   Q50%  |   Q75%  |   Q90%  | St. Dev.");
            for (int i = 0; i < group.Value.Count; ++i) {
                sb.AppendLine(group.Value[i].ToString(maxNameSize));
            }

            sb.AppendLine(new string('▔', linesize));
        }

        return sb.ToString();
    }
    private void InternalRun(string group, string name, Action fun, int batchesCount, int itersCountPerBatch) {
        // Register result
        if (!_groupValues.ContainsKey(group)) {
            _groupValues.Add(group, new List<Stats>());
        }

        // Benchmark

        long[] ticks = new long[batchesCount];
        Stopwatch timer = new();
        for (int i = 0; i < batchesCount; ++i) {
            timer.Restart();
            for (int j = 0; j < itersCountPerBatch; ++j) {
                fun();
            }

            timer.Stop();
            ticks[i] = timer.ElapsedTicks / itersCountPerBatch;
        }

        // Add results
        _groupValues[group].Add(new Stats(name, itersCountPerBatch, ticks));
    }

    private (int length, long duration) EstimateBatchLengthAndDuration(Action fun) {
        Stopwatch measure = Stopwatch.StartNew();
        fun();

        measure.Stop();
        int nbiters = (int)_maxDurationAutoBatch / (int)(measure.ElapsedTicks * 30);
        nbiters = Math.Max(Math.Min(nbiters, _maxItersCount), _minItersCount);
        return (nbiters, nbiters * measure.ElapsedTicks);
    }

    ///<summary>Convert a <see cref="System.TimeSpan"/> to <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    /// <param name="time">a <see cref="System.TimeSpan"/>.</param>
    /// <returns>the number of ticks of equivalent duration for a <see cref="System.Diagnostics.Stopwatch"/>.</returns>
    private long HigherPrecision(TimeSpan time) {
        return time.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
    }

    ///<summary>Convert a <see cref="System.Diagnostics.Stopwatch"/> ticks to a <see cref="System.TimeSpan"/>.</summary>
    /// <param name="ticks">The stopwatch number of ticks.</param>
    /// <returns>A <see cref="System.TimeSpan"/> of equivalent duration.</returns>
    private TimeSpan LowerPrecision(long ticks) {
        return TimeSpan.FromTicks(ticks * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    }

    private void SetThreadAffinity() {
        ProcessThreadCollection threads = Process.GetCurrentProcess().Threads;
        for (int i = 0; i < threads.Count; i++) {
            if (OperatingSystem.IsWindows()) {
                threads[i].ProcessorAffinity = (IntPtr) 0x0001;
            }
        }

        if (OperatingSystem.IsLinux()) {
            ulong processorMask = 0x0001UL;
            sched_setaffinity(0, new IntPtr(sizeof(ulong)), ref processorMask);
        }
    }


    [DllImport("libc.so.6", SetLastError = true)]
    private extern static int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong cpuset);

    // default settings
    // settings per bench
    // stats (can select which one to display)
    // print tables (nicely, aligned, nice symbols)
    // state object that can start measurement (faked, instead measure time until fake start and make diff, etc...)
    // run Action<State, object[]> in x batches of p iterations

    // At start sleep 100ms to stop JIT-0
    // GROUPS:
    // Bench are associated to a group
    // Bench without a group are added to the global group
    // A bench can be base of group or not
    //  - only one base per group (throw otherwise)
    //  - base is always printed first
    //  - base is is compared to other IC a star is printed if diff is significant, an '=' otherwise
    //  - base is always first bench of group
    //
    // BATCHS:
    // b batches of p iterations.
    // before running the actual benchmark, we run an estimating un.
    // the estimated time will help determine the execution plan.
    // Perform p (nb iters per batch) estimation:
    // nbIters = min(max / (t * nbBatch), 100)
    // if 'estimated time' >= 10 * clock precision
    //     1. p is 1
    // else
    //    1. if estimated time >= clock precision, set p to 10
    //    2. else rerun a 100 times (in this case, benchmark is fast so who cares ?)
    //    3. it is still slower than clock precision rerun 10*times more estimations until it works
    //    4. set p to the number of the minimal number of repetitions required to have times(p) > 10*clock precision
    // Perform b estimation (number of batches):
    //
    // b = targetedTime / estimated time per batch (p*t(p))
    //
    // if b is less than 30 force b to 30 and warn the user about longer exec time
    // if total estimated time > max time
    //      throw too long bench
    //
    // Procedure of measurement
    // - wait a 100ms
    // - run the benchmark function 31 times before anything else (enable tier2 JIT)
    //
}
