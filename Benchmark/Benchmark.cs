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
// - shuffle
// - run the b batches of p iterations
//    - for each batch store its average response time
// - compute stats
/*
BENCH NAME▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁
name   | batches,iterations | Executions/s | Average |   Min   |   Q25%  |   Q50%  |   Q75%  |   Max   | St. Dev.
{name} | 0000000,0000000000 | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un
▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔
*/


namespace Featurless.Benchmark;


#region

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

#endregion

/// <summary> This class run methods benchmarks and display results in summary statistic tables. </summary>
public sealed class Benchmarker
{
#nullable disable
    private const int _jitTierUpCount = 31;
    private const int _maxMeasuresCount = 1500;
    private const int _minItersCount = 30;

    private readonly long _maxDurationAutoBench;
    private readonly List<Plan> _plans;
    private readonly Dictionary<string, List<Statistics>> _stats;
    private readonly TextWriter _output;
    /// <summary> Aimed maximal duration of autoconfigured benchmarks. </summary>
    public TimeSpan MaxDurationAutoConfig {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Benchmarker.ToTimeSpan(_maxDurationAutoBench);
    }

    #nullable enable
    /// <inheritdoc />
    /// <summary> Initialize a new instance of <see cref="T:Featurless.Benchmark.Benchmarker" />. </summary>
    public Benchmarker(TextWriter? outputStream = null)
            : this(TimeSpan.FromSeconds(60), outputStream)
    {}

    /// <summary> Initialize a new instance of <see cref="Featurless.Benchmark.Benchmarker" />. </summary>
    /// <param name="autoBenchMaxDuration"> The aimed duration of autoconfigured benchmarks. </param>
    /// <param name="outputStream"> Where to print the results (stdout by default). </param>
    public Benchmarker(TimeSpan autoBenchMaxDuration, TextWriter? outputStream = null) {
        if (outputStream == null) {
            outputStream = Console.Out;
        }

        _plans = new List<Plan>();
        _stats = new Dictionary<string, List<Statistics>>();
        _maxDurationAutoBench = Benchmarker.ToStopwatchTicks(autoBenchMaxDuration);
        _output = outputStream;

        int coresCount = Math.Max(Environment.ProcessorCount - 1, val2: 1);
        _output.WriteLine($"[BENCHMARK] {coresCount} cores available for multi thread executions.");

        // jit optimize self
        Plan emptyPlan = new(group: null!, name: null!, function: static () => { },
                                           new Options(measuresCount: 30, itersCountPerMeasure: 30));
        Benchmarker.JitOptimize(() => emptyPlan.Run());
    }
#nullable disable

    /// <summary> Register a <see cref="System.Action" /> benchmark. The benchmark plan is automatically done. </summary>
    /// <param name="group"> The table in which will be displayed the benchmark. </param>
    /// <param name="name"> The displayed name of the benchmark. </param>
    /// <param name="fun"> The delegate to be benchmarked. </param>
    public void Register(string group, string name, Action fun) {
        if (!_stats.ContainsKey(group)) {
            _stats.Add(group, new List<Statistics>());
        }

        // Tiered Jit Preparation
        (int iterCount, int batchCount, long batchDuration) = EstimateItersPerMeasureAndDuration(fun);
        if (batchCount < 30) {
            _output.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Warning: Very small batch count: ({batchCount} < 30).");
        }
        Benchmarker.JitOptimize(fun);

        TimeSpan estimatedTime = Benchmarker.ToTimeSpan(batchCount * batchDuration);
        _output.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} ({batchCount}x{iterCount}, estimated time: {estimatedTime.TotalSeconds:G4}s).");
        _plans.Add(new Plan(group, name, fun, new Options(batchCount, iterCount)));
    }

    /// <summary> Register a <see cref="System.Action" /> benchmark. </summary>
    /// <param name="group"> The table in which will be displayed the benchmark. </param>
    /// <param name="name"> The displayed name of the benchmark. </param>
    /// <param name="fun"> The delegate to be benchmarked. </param>
    /// <param name="opts"> A <see cref="Options" /> instance. </param>
    public void Register(string group, string name, Action fun, Options opts) {
        if (!_stats.ContainsKey(group)) {
            _stats.Add(group, new List<Statistics>());
        }

        if (opts.MeasuresCount < 30) {
            _output.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Warning: Very small batch count: ({opts.MeasuresCount} < 30).");
        }

        if (opts.ItersCountPerMeasure < 1) {
            _output.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} - Warning: Very few iterationsper batch: ({opts.ItersCountPerMeasure} < 1).");
        }

        Benchmarker.JitOptimize(fun);
        _output.WriteLine($"[BENCHMARK::REGISTER] {group}.{name} ({opts.MeasuresCount}x{opts.ItersCountPerMeasure}).");
        _plans.Add(new Plan(group, name, fun, opts));
    }

    /// <summary> Run all the previously registered benchmarks. </summary>
    public void Run() {
        FisherYatesShuffle(_plans);

        // setup benchmarking process state
        nint procAffinity = -1;
        ProcessPriorityClass processPriority = Process.GetCurrentProcess().PriorityClass;
        ThreadPriority threadPriority = Thread.CurrentThread.Priority;

        bool changeAffinity = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                           || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        // prefer the second processor for running benchmarks
        if (changeAffinity) {
#pragma warning disable CA1416 // os check done with changeAffinity
            _output.WriteLine("[BENCHMARK::RUN] CPU affinity set.");
            procAffinity = Process.GetCurrentProcess().ProcessorAffinity;
            Process.GetCurrentProcess().ProcessorAffinity = new nint(2);
#pragma warning restore CA1416
        }
        else {
            _output.WriteLine("[BENCHMARK::RUN] No CPU affinity set.");
        }

        bool changePriority = IsRoot();
        // prevent normal priority threads to interrupt the benchmark
        if (changePriority) {
            _output.WriteLine("[BENCHMARK::RUN] Thread priority set.");
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        } else {
            _output.WriteLine("[BENCHMARK::RUN] No thread priority set.");
        }

        // perform benchmarks
        ReadOnlySpan<Plan> plans = CollectionsMarshal.AsSpan(_plans);
        CpuWarmUp();
        for (int i = 0; i < plans.Length; ++i) {
            ref readonly Plan plan = ref plans[i];
            _output.Write("[BENCHMARK::RUN] " + plan + "...");
            Statistics currentStats = plan.Run();
            _stats[plan.Group].Add(currentStats);
            _output.WriteLine("done in " + currentStats.GetTotalRunningTime() + '.');
        }

        // reorder results per group in registering order
        foreach (List<Statistics> list in _stats.Values) {
            list.Sort(static (el1, el2) => el1.Order - el2.Order);
        }

        // restore previous process state
        if (changeAffinity) {
#pragma warning disable CA1416  // os check done with changeAffinity
            Process.GetCurrentProcess().ProcessorAffinity = procAffinity;
#pragma warning restore CA1416
        }

        if (changePriority) {
            Process.GetCurrentProcess().PriorityClass = processPriority;
            Thread.CurrentThread.Priority = threadPriority;
        }
    }

    /// <summary>
    ///     Convert this instance of <see cref="Featurless.Benchmark.Benchmarker" /> to a string summary of the already
    ///     performed benchmarks.
    /// </summary>
    /// <returns> The string summary. </returns>
    public override string ToString() {
        StringBuilder sb = new(4_096);
        foreach (KeyValuePair<string, List<Statistics>> group in _stats) {
            sb.AppendLine();
            int maxNameSize = 5;
            for (int i = 0; i < group.Value.Count; ++i) {
                maxNameSize = Math.Max(maxNameSize, group.Value[i].Name.Length + 1);
            }

            sb.Append("BENCHMARK▁");
            sb.AppendLine(group.Key.ToUpper()
                               .Replace(oldChar: ' ', newChar: '▁')
                               .PadRight(maxNameSize + 96, paddingChar: '▁'));
            sb.Append("Name".PadRight(maxNameSize));
            sb.AppendLine("| batches,iterations | Executions/s | Average |   Q10%  |   Q25%  |   Q50%  |   Q75%  |   Q90%  | St. Dev.");
            for (int i = 0; i < group.Value.Count; ++i) {
                group.Value[i].AppendToString(sb, maxNameSize);
            }

            sb.AppendLine(new string(c: '▔', maxNameSize + 106));
        }

        return sb.ToString();
    }

    /// <summary> Estimate the number of iterations per measure and the duration of a measure. </summary>
    /// <param name="fun"> The benchmarked function. </param>
    /// <returns> A tuple containing the length and the duration. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (int length, int batchCount, long duration) EstimateItersPerMeasureAndDuration(Action fun) {
        Stopwatch measure = Stopwatch.StartNew();
        fun();
        measure.Stop();

        int iterCount = (int)(_maxDurationAutoBench / measure.ElapsedTicks);
        int batchCount = Math.Min((int) iterCount / 30, Benchmarker._maxMeasuresCount);
        iterCount = Math.Max(iterCount / batchCount, 1);
        return (iterCount, batchCount, iterCount * measure.ElapsedTicks);
    }

    /// <summary> Randomize elements order in a <see cref="System.Collections.Generic.List{T}" />. </summary>
    /// <param name="list"> The list to randomize. </param>
    /// <typeparam name="T"> The type of elements in the list. </typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void FisherYatesShuffle<T>(List<T> list) {
        _output.WriteLine("[BENCHMARK::SHUFFLE] Shuffling benchmarks.");
        Random rnd = new();
        int count = list.Count;
        int last = count - 1;
        for (int i = 0; i < last; ++i) {
            int r = rnd.Next(i, count);
            (list[i], list[r]) = (list[r], list[i]);
        }
    }

    /// <summary> Convert a <see cref="System.TimeSpan" /> to <see cref="System.Diagnostics.Stopwatch" /> ticks. </summary>
    /// <param name="time"> a <see cref="System.TimeSpan" />. </param>
    /// <returns> the number of ticks of equivalent duration for a <see cref="System.Diagnostics.Stopwatch" />. </returns>
    private static long ToStopwatchTicks(TimeSpan time) {
        return time.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
    }

    /// <summary> Convert a <see cref="System.Diagnostics.Stopwatch" /> ticks to a <see cref="System.TimeSpan" />. </summary>
    /// <param name="ticks"> The stopwatch number of ticks. </param>
    /// <returns> A <see cref="System.TimeSpan" /> of equivalent duration. </returns>
    private static TimeSpan ToTimeSpan(long ticks) {
        return TimeSpan.FromTicks(ticks * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    }

    /// <summary> Try to induce the JIT-compiler to increase the compilation tier of an <see cref="System.Action" />. </summary>
    /// <param name="action"> The action to upgrade </param>
    private static void JitOptimize(Action action) {
        // tiered compilation occurs only if function executed at least 30 times
        for (int i = 0; i < Benchmarker._jitTierUpCount; ++i) {
            action();
        }

        // tiered compilation trigger only if the JIT did not compiled anything for at least 100ms.
        Thread.Sleep(222);
    }

    [MethodImpl(MethodImplOptions.NoOptimization)]
    private long CpuWarmUp() {
        _output.Write("[BENCHMARK::CPUWARMUP] Perform CPU Warmup...");
        int i = 0;
        long res = 0;
        Random rnd = new();
        while (i++ < Int32.MaxValue  / 2) {
            res += rnd.NextInt64();
        }

        _output.WriteLine("Done.");
        return res;
    }

    private static bool IsRoot() {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        return geteuid() == 0;

        [DllImport("libc")]
        static extern uint geteuid();
    }
}
