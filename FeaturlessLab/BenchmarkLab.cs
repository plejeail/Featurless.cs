// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable UnusedVariable
namespace FeaturlessLab;

using Featurless;

public static class BenchmarkLab
{
    private static void Sum(int[] arr) {
        for (int i = 1; i < arr.Length; ++i) {
            arr[i] += arr[i-1];
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
            arr[i] *= arr[i-1];
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

    private static void None() {}

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

        int[] arr1 = CreateArray(20000);
        int[] arr2 = CreateArray(20000);
        int[] arr3 = CreateArray(20000);
        int[] arr4 = CreateArray(20000);
        bench.Register("test sum", "sum 1", () => Sum(arr1));
        bench.Register("test mult", "mult 1", () => Mult(arr2));
        bench.Register("test sum", "sum 2", () => Sum2(arr3));
        bench.Register("test mult", "mult 2",  () => Mult2(arr4));
        bench.Register("test sum", "sum 1", () => Sum(arr1));
        bench.Register("test mult", "mult 1", () => Mult(arr2));
        bench.Register("test sum", "sum 2", () => Sum2(arr3));
        bench.Register("test mult", "mult 2", () => Mult2(arr4));
        bench.Register("test mult", "None", None);
        bench.Register("test sum", "None", None);
        bench.Run();
        Console.WriteLine(bench);
    }
}
