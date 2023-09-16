namespace Featurless.Benchmark;


using System.Diagnostics;
using System.Runtime.CompilerServices;


/// <summary> Plan for playing a registered benchmark. </summary>
readonly struct Plan
{
    private static int _nextOrderValue /* = 0*/;
    internal readonly string Group;
    private readonly bool _isMultiThread;
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
    internal Plan(string group, string name, Action function, Options opts) {
        Group = group;
        _name = name;
        _function = function;
        _measuresCount = opts.MeasuresCount;
        _itersPerMeasure = opts.ItersCountPerMeasure;
        _isMultiThread = opts.MultiThread;
        _order = Plan._nextOrderValue++;
    }

    /// <summary>
    ///     Converts this <see cref="Plan" /> instance to its equivalent
    ///     string representation.
    /// </summary>
    /// <returns> The string representation of this instance. </returns>
    public override string ToString() {
        return $"{Group}::{_name} ({_measuresCount}x{_itersPerMeasure})";
    }

    /// <summary> Run the benchmark. </summary>
    /// <returns> The benchmark execution statistics. </returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal Statistics Run() {
        long[] ticks = new long[_measuresCount];
        int processorCount;
        int iters = _itersPerMeasure;
        Action f = _function;

        // code warm-up
        _function();

        if (_isMultiThread) {
            processorCount = Math.Max(Environment.ProcessorCount - 1, val2: 1);
            ParallelOptions options = new() { MaxDegreeOfParallelism = processorCount, };
            Parallel.For(fromInclusive: 0, _measuresCount, options, body: i => {
                Stopwatch timer = Stopwatch.StartNew();
                for (int j = 0; j < iters; ++j) {
                    f();
                }

                ticks[i] = timer.ElapsedTicks / iters;
            });
        } else {
            processorCount = 1;
            Stopwatch timer = new();
            for (int i = 0; i < _measuresCount; ++i) {
                timer.Restart();
                for (int j = 0; j < _itersPerMeasure; ++j) {
                    _function();
                }

                timer.Stop();
                ticks[i] = timer.ElapsedTicks / _itersPerMeasure;
            }
        }

        return new Statistics(_name, _order, _itersPerMeasure, processorCount, ticks);
    }
}
