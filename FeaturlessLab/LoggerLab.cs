namespace FeaturlessLab;

using Featurless;

public class LoggerLab
{
    public static void Run(bool multiThread = true) {

        RunSingleThread(20000, 1000, 10);
        Console.WriteLine("####################");
        RunMultiThread(20, 1000, 1000, 10);
    }

    private static void RunSingleThread(int count, int fileSize, int maxFiles) {
        Console.WriteLine("### Single thread ###");
        using Logger l = new ("./", "logger-test-st", fileSize, maxFiles);
        const string msg = "OK my message is this: 'GET UP Man!'OK my message is this: 'GET UP Man!'";
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; ++i) {
            l.Error(msg);
        }
        if (sw.ElapsedMilliseconds > 0) {
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"logs/s: {1000 * count / sw.ElapsedMilliseconds}");
            Console.WriteLine($"ns/log: {sw.ElapsedMilliseconds * 1000000 / count}");
            Console.WriteLine($"MB/s:  {count * sizeof(char) * msg.Length / (1000.0d * sw.ElapsedMilliseconds):F2}");
        } else {
            Console.WriteLine("Faster than measure");
        }
    }

    private static void RunMultiThread(int nbTasks, int nLogsPerTask, int fileSize, int maxFiles) {
        if (nbTasks <= 0 || nLogsPerTask <= 0) {
            return;
        }
        Console.WriteLine("### Multi thread ###");
        Logger l = new ("./", "logger-test-mt", fileSize, maxFiles);
        l.MinLevel = Logger.Level.Debug;
        const string str = "Allo Allo ? cest ici que ca se passe, pas par la bas man!";
        Task[] logs = new Task[nbTasks];
        int done = 0;
        for (int i = 0; i < logs.Length; ++i) {
            logs[i] = new Task(() => {
                for (int k = 0; k < nLogsPerTask; ++k) {
                    // ReSharper disable once AccessToDisposedClosure
                    l.Debug(str);
                }
                Interlocked.Increment(ref done);
            });
        }

        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < logs.Length; ++i) {
            logs[i].Start();
        }

        while (done != nbTasks) {
            Thread.Sleep(1);
        }
        l.Dispose();
        if (sw.ElapsedMilliseconds > 0) {
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"logs/s: {1000 * nbTasks * nLogsPerTask / sw.ElapsedMilliseconds}");
            Console.WriteLine($"ns/log: {sw.ElapsedMilliseconds * 1000000 / (nbTasks * nLogsPerTask)}");
            Console.WriteLine($"MB/s:  {nbTasks * nLogsPerTask * sizeof(char) * str.Length / (1000.0d * sw.ElapsedMilliseconds):F2}");
        } else {
            Console.WriteLine("Faster than measure");
        }
    }
}
