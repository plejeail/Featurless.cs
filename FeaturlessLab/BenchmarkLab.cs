namespace FeaturlessLab;

using Featurless;

public class BenchmarkLab
{
    private static void Sum(int[] arr) {
        for (int i = 1; i < arr.Length; ++i) {
            arr[i] += arr[i-1];
        }
    }

    private static void Mult(int[] arr) {
        for (int i = 2; i < arr.Length; ++i) {
            arr[i] *= arr[i-1];
        }
    }

    private static void None() {}

    public static void Run() {
        Benchmark bench = new();

        int[] arrayInt = new int[20000];
        for (int i = 0; i < arrayInt.Length; ++i) {
            arrayInt[i] = i;
        }


        bench.Run("test", "sum 1", () => Sum(arrayInt));
        bench.Run("test", "sum 1", () => Sum(arrayInt));
        bench.Run("test", "mult",  () => Mult(arrayInt));
        bench.Run("test", "mult", () => Mult(arrayInt));
        bench.Run("test", "sum 2", () => Sum(arrayInt));
        bench.Run("test", "nothing", None);
        bench.Run("test", "nothing", None);

        Console.WriteLine(bench);
    }
}
