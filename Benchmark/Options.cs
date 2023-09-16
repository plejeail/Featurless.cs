namespace Featurless.Benchmark;


/// <summary> Provide options to be used when registering a benchmark in <see cref="Featurless.Benchmark.Benchmarker" />. </summary>
public readonly struct Options
{
    /// <summary> The number of measures to be computed by the <see cref="Featurless.Benchmark.Benchmarker" />. </summary>
    public readonly int MeasuresCount;
    /// <summary> The number of times to call the benchmarked method per measure. </summary>
    public readonly int ItersCountPerMeasure;
    /// <summary> Run all measures in a task at the same time. </summary>
    public readonly bool MultiThread;

    /// <summary> Initialize a new instance of <see cref="Options" />. </summary>
    /// <param name="measuresCount">
    ///     The number of measures to be computed by the
    ///     <see cref="Featurless.Benchmark.Benchmarker" />.
    /// </param>
    /// <param name="itersCountPerMeasure"> The number of times to call the benchmarked method per measure. </param>
    /// <param name="multiThread"> If true, run all measures in a task at the same time. </param>
    public Options(int measuresCount, int itersCountPerMeasure, bool multiThread = false) {
        MeasuresCount = measuresCount;
        ItersCountPerMeasure = itersCountPerMeasure;
        MultiThread = multiThread;
    }
}
