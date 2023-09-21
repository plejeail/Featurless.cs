namespace Featurless.Benchmark;


using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;


/// <summary> Statistics computed from measures computed from a <see cref="Plan" />. </summary>
readonly struct Statistics
{
    /// <summary> Name of the benchmark displayed in the results table. </summary>
    internal readonly string Name;
    /// <summary> Ordering value of the Statistics instance. Used to retrieve registering order. </summary>
    internal readonly int Order;
    private readonly int _measuresCount;
    private readonly int _processorCount;
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
    /// <param name="processorCount"> The number of cores used. </param>
    /// <param name="measures"> An array of measured ticks (from stopwatch precision). </param>
    /// <param name="order"> The 'order rank' in the final table. </param>
    internal Statistics(string name, int order, int itersPerMeasure, int processorCount, long[] measures) {
        Name = name;
        Order = order;
        _itersPerMeasure = itersPerMeasure;
        _measuresCount = measures.Length - 1;
        _processorCount = processorCount;
        Array.Sort(measures);
        _average = 0;
        for (int i = 0; i < _measuresCount; ++i) {
            _average = _average + measures[i];
        }
        _average = _average / measures.Length;

        long stdev = 0;
        for (int i = 0; i < _measuresCount; ++i) {
            long value = measures[i] - _average;
            stdev = stdev + value * value;
        }

        stdev = stdev / (_measuresCount - 1);
        _stdev = (long)Math.Sqrt(stdev);
        _q10 = measures[measures.Length / 10];
        _q25 = measures[measures.Length / 4];
        _q50 = measures[measures.Length / 2];
        _q75 = measures[3 * measures.Length / 4];
        _q90 = measures[9 * measures.Length / 10];
    }

    /// <summary> Re-compute total benchmarking time. </summary>
    /// <returns> Formatted string of the benchmark running time. </returns>
    internal string GetTotalRunningTime() {
        return Statistics.FormatTicks(_average * _itersPerMeasure * _measuresCount / _processorCount);
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
        sb.Append(ExecsPerSecond().ToString("G4").PadLeft(10));
        sb.Append("/s | ");
        sb.Append(Statistics.FormatTicks(_average));
        sb.Append(" | ");
        sb.Append(Statistics.FormatTicks(_q10));
        sb.Append(" | ");
        sb.Append(Statistics.FormatTicks(_q25));
        sb.Append(" | ");
        sb.Append(Statistics.FormatTicks(_q50));
        sb.Append(" | ");
        sb.Append(Statistics.FormatTicks(_q75));
        sb.Append(" | ");
        sb.Append(Statistics.FormatTicks(_q90));
        sb.Append(" | ");
        sb.AppendLine(Statistics.FormatTicks(_stdev));
    }

    /// <summary> Compute the ExecsPerSecond statistic. </summary>
    /// <returns> The estimated maximum number of possible executions per seconds. </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private double ExecsPerSecond() {
        return _processorCount * ((double)Stopwatch.Frequency / _average);
    }

    /// <summary> Convert a number of stopwatch ticks to the appropriate time unit. </summary>
    /// <param name="ticks"> The number of ticks to convert. </param>
    /// <returns> A formated string represented the number of ticks. </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string FormatTicks(long ticks) {
        long nanos = (long)(1_000_000_000D / Stopwatch.Frequency) * ticks;
        if (nanos < 1000L) {
            return nanos.ToString(CultureInfo.InvariantCulture).PadLeft(5) + "ns";
        }

        if (nanos < 1_000_000L) {
            double nsd1 = (double)nanos / 1000L;
            return nsd1.ToString(format: "G4", CultureInfo.InvariantCulture).PadLeft(5) + "μs";
        }

        if (nanos < 1_000_000_000L) {
            double nsd2 = (double)nanos / 1_000_000L;
            return nsd2.ToString(format: "G4", CultureInfo.InvariantCulture).PadLeft(5) + "ms";
        }

        double nsd = (double)nanos / 1_000_000_000L;
        return nsd.ToString(format: "G4", CultureInfo.InvariantCulture).PadLeft(5) + 's';
    }
}
