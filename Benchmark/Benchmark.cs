// Too Fast not done => rework planning
// nb iters => rework planning (set min to 100 if possible, i.e 100*30*exec time < max + lower if not the case)
//               (it may help for st dev)
//
namespace Featurless;

using System.Diagnostics;
using System.Globalization;
using System.Text;

public class Benchmark
{
#nullable disable
    private readonly struct Stats
    {
        internal enum Status { Ok, TooFast, NotEnoughBatches, };
        public readonly string Name;
        private readonly Status _status;
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
            _status = Status.Ok;
            _itersPerBatch = itersPerBatch;
            Array.Sort(ticks);

            _batchesCount = ticks.Length;
            _standardDeviation = 0L;
            _averageTime = 0L;
            for (int i = 0; i < _batchesCount; ++i) {
                _averageTime = _averageTime + ticks[i];
            }
            _averageTime = _averageTime / _batchesCount;

            for (int i = 0; i < _batchesCount; ++i) {
                long t = (ticks[i] - _averageTime);
                _standardDeviation += t * t;
            }
            _standardDeviation = _standardDeviation / (_batchesCount - 1);
            _standardDeviation = (long)Math.Sqrt(_standardDeviation);

            _q10 = ticks[_batchesCount / 10];
            _q25 = ticks[_batchesCount / 4];
            _q50 = ticks[_batchesCount / 2];
            _q75 = ticks[3 * _batchesCount / 4];
            _q90 = ticks[9 * _batchesCount / 10];
        }
        internal Stats(string name, Status status) {
            Name = name;
            _status = status;
            _itersPerBatch = _batchesCount = 0;
            _standardDeviation = _averageTime = _q10 = _q25 = _q50 = _q75 = _q90 = 0L;
        }

        public string ToString(int nameLength) {
            StringBuilder sb = new();
            sb.Append(Name.PadRight(nameLength));
            sb.Append("| ");
            if (_status == Status.TooFast) {
                sb.Append("execution is too fast.");
                return sb.ToString();
            } else if (_status == Status.NotEnoughBatches) {
                sb.Append("Not enough measures.");
                return sb.ToString();
            }

            sb.Append(_batchesCount.ToString().PadLeft(7));
            sb.Append(',');
            sb.Append(_itersPerBatch.ToString().PadRight(11));
            sb.Append("| ");
            double execsPerSec = 1_000_000D * TimeSpan.TicksPerMillisecond / _averageTime;
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

        private string FormatTime(long ticks, int length) {
            long nanos = 1_000L * ticks / TimeSpan.TicksPerMillisecond;
            if (nanos < 1000) {
                return nanos.ToString(CultureInfo.InvariantCulture).PadLeft(length+1) + "ns";
            } else if (nanos < 1_000_000) {
                double nsd = (double) nanos / 1000;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + "μs";
            } else if (nanos < 1_000_000_000) {
                double nsd = (double) nanos / 1_000_000;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + "ms";
            } else if (nanos < 1_000_000_000_000) {
                double nsd = (double) nanos / 1_000_000_000;
                return nsd.ToString($"G{length}", CultureInfo.InvariantCulture).PadLeft(length + 1) + 's';
            } else { // #E+0
                double nsd = (double) nanos / 1_000_000_000;
                return nsd.ToString("#.#e0", CultureInfo.InvariantCulture).PadLeft(length + 1) + 's';
            }
        }
    }

    private readonly long _maxDurationAutoBatch;
    private readonly Dictionary<string, List<Stats>> _groupValues;

    public Benchmark() : this(TimeSpan.FromSeconds(30))
    {

    }

    public Benchmark(TimeSpan autoBatchMaxDuration) {
        _groupValues = new Dictionary<string, List<Stats>>();
        _maxDurationAutoBatch = autoBatchMaxDuration.Ticks;

        Thread.Sleep(200); // to be sure that tiered jit will be available

        // make itself candidate for tiered JIT
        _groupValues.Add(String.Empty, new List<Stats>());
        for (int i = 0; i < 31; ++i) {
            InternalRun(String.Empty, String.Empty, () => {}, 31, 31);
        }
        _groupValues.Remove(String.Empty);
    }

    public void Run(string group, string name, Action fun) {
        // Register result
        if (!_groupValues.ContainsKey(group)) {
            _groupValues.Add(group, new List<Stats>());
        }

        // Tiered Jit Preparation
        for (int i = 0; i < 30; ++i) {
            fun();  // Force Tier 1 Jit
        }

        (int itersCountPerBatch, long batchDuration) = EstimateBatchLengthAndDuration(fun);
        Debug.Assert(itersCountPerBatch != 0, "batch size should not be 0");
        if (itersCountPerBatch == -1) {
            _groupValues[group].Add(new Stats(name, Stats.Status.TooFast));
            return;
        }

        int batchesCount = Math.Min((int)(_maxDurationAutoBatch / batchDuration), 10000);
        InternalRun(group, name, fun, batchesCount, itersCountPerBatch);
    }

    public void Run(string group, string name, Action fun, int batchesCount, int itersCountPerBatch) {
        // Register result
        if (!_groupValues.ContainsKey(group)) {
            _groupValues.Add(group, new List<Stats>());
        }
        // Tiered Jit Preparation
        for (int i = 0; i < 31; ++i) {
            fun(); // Force Tier 1 Jit
        }

        InternalRun(group, name, fun, batchesCount, itersCountPerBatch);
    }

    private void InternalRun(string group, string name, Action fun, int batchesCount, int itersCountPerBatch) {
        if (batchesCount < 30) {
            _groupValues[group].Add(new Stats(name, Stats.Status.NotEnoughBatches));
            return;
        }

        long[] ticks = new long[batchesCount];
        Stopwatch timer = new();
        for (int i = 0; i < batchesCount; ++i) {
            timer.Restart();
            for (int j = 0; j < itersCountPerBatch; ++j) {
                fun();
            }
            ticks[i] = timer.ElapsedTicks / itersCountPerBatch;
        }

        // Benchmark
        _groupValues[group].Add(new Stats(name, itersCountPerBatch, ticks));
    }

    private (int length, long duration) EstimateBatchLengthAndDuration(Action fun) {
        Stopwatch measure = Stopwatch.StartNew();
        fun();
        measure.Stop();

        if (measure.ElapsedTicks >= 100) {
            return (1, measure.ElapsedTicks);
        } else if (measure.ElapsedTicks > 0) {
            return (1 + 100 / (int)measure.ElapsedTicks, measure.ElapsedTicks);
        }

        measure.Restart();
        for (int i = 0; i < 100; ++i) {
            fun();
        }
        measure.Stop();

        if (measure.ElapsedTicks >= 100) {
            return (100, measure.ElapsedTicks);
        } else if (measure.ElapsedTicks > 0) {
            return (100 + 100 / (int) measure.ElapsedTicks, measure.ElapsedTicks);
        } else {
            return (-1, -1);
        }
    }

    public override string ToString() {
        StringBuilder sb = new(1280);
        foreach (KeyValuePair<string, List<Stats>> group in _groupValues) {
            int maxNameSize = 5;
            for (int i = 0; i < group.Value.Count; ++i) {
                maxNameSize = Math.Max(maxNameSize, group.Value[i].Name.Length + 1);
            }
            int linesize = maxNameSize + 108;
            sb.AppendLine(group.Key.PadRight(linesize, '▁'));
            sb.Append("Name".PadRight(maxNameSize));
            sb.AppendLine("| batches,iterations | Executions/s | Average |   Q10%  |   Q25%  |   Q50%  |   Q75%  |   Q90%  | St. Dev.");
            for (int i = 0; i < group.Value.Count; ++i) {
                sb.AppendLine(group.Value[i].ToString(maxNameSize));
            }
            sb.AppendLine(new string('▔', linesize));
        }

        return sb.ToString();
    }

/*
▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁
name   | batches,iterations | Executions/s | Average |   Min   |   Q25%  |   Q50%  |   Q75%  |   Max   | St. Dev. | Significant
{name} | 0000000,0000000000 | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un  |      —
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un  |      ✔      
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un  |      ✘      
{name} |    0000,0000       | 1000000000/s | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un | 0.000un  |      ✔      
▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔▔
*/
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
