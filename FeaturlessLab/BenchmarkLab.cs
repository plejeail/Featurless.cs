// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedVariable


namespace FeaturlessLab;


#region

using Featurless.Benchmark;

#endregion

public static class BenchmarkLab
{
    private static void Sum(int[] arr) {
        for (int i = 1; i < arr.Length; ++i) {
            arr[i] += arr[i - 1];
        }
    }

    private static unsafe void Sum2(int[] arr) {
        fixed (int* arrPtrFixed = arr) {
            int* arrPtr = arrPtrFixed;
            for (int i = 1; i < arr.Length; ++i) {
                *++arrPtr += *arrPtr;
            }
        }
    }

    private static void Mult(int[] arr) {
        for (int i = 2; i < arr.Length; ++i) {
            arr[i] *= arr[i - 1];
        }
    }

    private static unsafe void Mult2(int[] arr) {
        fixed (int* arrPtrFixed = arr) {
            int* arrPtr = arrPtrFixed;
            for (int i = 2; i < arr.Length; ++i) {
                *++arrPtr *= *arrPtr;
            }
        }
    }

    private static void None() { }

    private static int[] CreateArray(int size) {
        int[] arrayInt = new int[size];
        for (int i = 0; i < arrayInt.Length; ++i) {
            arrayInt[i] = i;
        }

        return arrayInt;
    }

    public static void Run() {
        Benchmarker bench = new();
        Thread.Sleep(200);
        int[] arr1 = BenchmarkLab.CreateArray(20000);
        int[] arr2 = BenchmarkLab.CreateArray(20000);
        int[] arr3 = BenchmarkLab.CreateArray(20000);
        int[] arr4 = BenchmarkLab.CreateArray(20000);
        bench.Register(@group: "test sum", name: "sum 1", fun: () => BenchmarkLab.Sum(arr1));
        bench.Register(@group: "test mult", name: "mult 1", fun: () => BenchmarkLab.Mult(arr2));
        bench.Register(@group: "test sum", name: "sum 2", fun: () => BenchmarkLab.Sum2(arr3));
        bench.Register(@group: "test mult", name: "mult 2", fun: () => BenchmarkLab.Mult2(arr4));
        bench.Register(@group: "test sum", name: "sum 1", fun: () => BenchmarkLab.Sum(arr1));
        bench.Register(@group: "test mult", name: "mult 1", fun: () => BenchmarkLab.Mult(arr2));
        bench.Register(@group: "test sum", name: "sum 2", fun: () => BenchmarkLab.Sum2(arr3));
        bench.Register(@group: "test mult", name: "mult 2", fun: () => BenchmarkLab.Mult2(arr4));
        bench.Register(@group: "test mult", name: "None", BenchmarkLab.None);
        bench.Register(@group: "test sum", name: "None", BenchmarkLab.None);
        bench.Run();
        Console.WriteLine(bench);
    }
}
