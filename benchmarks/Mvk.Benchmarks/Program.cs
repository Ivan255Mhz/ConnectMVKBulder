using System.Diagnostics;
using System.Globalization;
using System.Text;
using Mvk.MVK01;

namespace Mvk.Benchmarks;

internal static class Program
{
    private const int DefaultIterations = 20;
    private const int DefaultWarmup = 3;
    private const int DefaultDelayMs = 5;
    private const int DefaultChannel = 1;
    private const int DefaultMeasurement = (int)Mvk01MeasurementNumber.Rms10To1000Acceleration;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        BenchmarkOptions options;
        try
        {
            options = BenchmarkOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintHelp();
            return 1;
        }

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        Console.WriteLine("MVK01 read-only benchmark");
        Console.WriteLine($"Hosts: {string.Join(", ", options.Hosts)}");
        Console.WriteLine(
            $"Iterations: {options.Iterations}, warmup: {options.Warmup}, delay: {options.DelayMs} ms");
        Console.WriteLine($"Channel: {options.Channel}, measurement: {options.Measurement}");
        Console.WriteLine();

        using CancellationTokenSource cancellation = new();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        List<Mvk01Connection> connections = [];
        try
        {
            foreach (string host in options.Hosts)
            {
                try
                {
                    Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                        host,
                        cancellationToken: cancellation.Token,
                        snapshotOptions: new Mvk01SnapshotOptions
                        {
                            Consistency = Mvk01SnapshotConsistencyMode.Checked
                        });

                    connections.Add(connection);

                    Mvk01DeviceInfo info = await connection.ReadDeviceInfoAsync(cancellation.Token);
                    Mvk01Channel snapshot = await connection.ReadChannelSnapshotAsync(
                        options.Channel,
                        cancellation.Token);

                    Console.WriteLine(
                        $"{host}: OK, firmware {info.FirmwareVersion}, S/N {info.SerialNumber}, "
                        + $"uptime {info.Uptime}, snapshot consistent={snapshot.IsConsistent}, "
                        + $"attempts={snapshot.ReadAttempts}");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    Console.WriteLine($"{host}: SKIP — {exception.GetType().Name}: {exception.Message}");
                }
            }

            if (connections.Count == 0)
            {
                Console.WriteLine("Нет доступных устройств.");
                return 2;
            }

            Console.WriteLine();
            List<BenchmarkResult> results = [];

            foreach (Mvk01Connection connection in connections)
            {
                results.AddRange(await RunDeviceAsync(connection, options, cancellation.Token));
            }

            PrintDeviceResults(results);
            PrintAggregate(results);

            if (options.RunParallel)
            {
                await RunParallelAsync(connections, options, cancellation.Token);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Остановлено.");
            return 130;
        }
        finally
        {
            foreach (Mvk01Connection connection in connections)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private static async Task<IReadOnlyList<BenchmarkResult>> RunDeviceAsync(
        Mvk01Connection connection,
        BenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        List<BenchmarkResult> results = [];

        foreach (BenchmarkCase benchmarkCase in CreateCases(options))
        {
            for (int warmup = 0; warmup < options.Warmup; warmup++)
            {
                try
                {
                    await benchmarkCase.Operation(connection, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    if (warmup == 0)
                    {
                        Console.Error.WriteLine(
                            $"{connection.Device.Id} {benchmarkCase.Name} warmup: "
                            + $"{exception.GetType().Name}: {exception.Message}");
                    }

                    break;
                }
            }

            List<double> latencies = new(options.Iterations);
            long allocatedBytes = 0;
            int errors = 0;

            for (int iteration = 0; iteration < options.Iterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
                long startedAt = Stopwatch.GetTimestamp();

                try
                {
                    await benchmarkCase.Operation(connection, cancellationToken);
                    latencies.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    errors++;

                    if (errors == 1)
                    {
                        Console.Error.WriteLine(
                            $"{connection.Device.Id} {benchmarkCase.Name}: "
                            + $"{exception.GetType().Name}: {exception.Message}");
                    }
                }
                finally
                {
                    allocatedBytes += Math.Max(
                        0,
                        GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
                }

                if (options.DelayMs > 0 && iteration + 1 < options.Iterations)
                {
                    await Task.Delay(options.DelayMs, cancellationToken);
                }
            }

            results.Add(new BenchmarkResult(
                connection.Device.Id,
                benchmarkCase.Name,
                benchmarkCase.RequestsPerOperation,
                latencies,
                options.Iterations,
                errors,
                allocatedBytes));
        }

        return results;
    }

    private static IReadOnlyList<BenchmarkCase> CreateCases(BenchmarkOptions options)
    {
        return
        [
            new("ReadDeviceInfo", 1, (connection, ct) => connection.ReadDeviceInfoAsync(ct)),
            new("ReadChannel (5 req)", 5, (connection, ct) => connection.ReadChannelAsync(options.Channel, ct)),
            new("ReadChannelSnapshot (3 req)", 3, (connection, ct) => connection.ReadChannelSnapshotAsync(options.Channel, ct)),
            new("ReadChannelConfig", 1, (connection, ct) => connection.ReadChannelConfigAsync(options.Channel, ct)),
            new("ReadTacho", 1, (connection, ct) => connection.ReadTachoAsync(1, ct)),
            new("ReadMeasurement", 1, (connection, ct) => connection.ReadValuesAsync(options.Measurement, ct))
        ];
    }

    private static async Task RunParallelAsync(
        IReadOnlyList<Mvk01Connection> connections,
        BenchmarkOptions options,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"Parallel measurement: {connections.Count} devices × {options.Iterations} reads");

        int errors = 0;
        long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(connections.Select(async connection =>
        {
            for (int iteration = 0; iteration < options.Iterations; iteration++)
            {
                try
                {
                    await connection.ReadValuesAsync(options.Measurement, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }));

        stopwatch.Stop();
        long allocatedBytes = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore);
        int totalReads = connections.Count * options.Iterations;
        double seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);

        Console.WriteLine(
            $"Total: {totalReads} reads, {seconds:F2} s, "
            + $"{totalReads / seconds:F1} reads/s, errors {errors}, "
            + $"alloc/read {allocatedBytes / (double)totalReads:F0} B");
    }

    private static void PrintDeviceResults(IReadOnlyList<BenchmarkResult> results)
    {
        Console.WriteLine("Per-device results");
        WriteHeader();

        foreach (BenchmarkResult result in results
            .OrderBy(item => item.Device, StringComparer.Ordinal)
            .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            WriteRow(result);
        }
    }

    private static void PrintAggregate(IReadOnlyList<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.WriteLine("Aggregate by operation");
        WriteHeader();

        foreach (IGrouping<string, BenchmarkResult> group in results
            .GroupBy(item => item.Name)
            .OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            List<double> latencies = group
                .SelectMany(item => item.Latencies)
                .ToList();

            BenchmarkResult aggregate = new(
                "*",
                group.Key,
                group.First().RequestsPerOperation,
                latencies,
                group.Sum(item => item.Attempts),
                group.Sum(item => item.Errors),
                group.Sum(item => item.AllocatedBytes));

            WriteRow(aggregate);
        }
    }

    private static void WriteHeader()
    {
        Console.WriteLine(
            $"{"Device",-16} {"Case",-28} {"N",4} {"Err",4} "
            + $"{"Mean",9} {"P50",9} {"P95",9} {"P99",9} {"Max",9} "
            + $"{"req/s",9} {"alloc/op",11}");
    }

    private static void WriteRow(BenchmarkResult result)
    {
        LatencyStats stats = result.Stats;

        Console.WriteLine(
            $"{result.Device,-16} {result.Name,-28} {stats.Count,4} {result.Errors,4} "
            + $"{stats.MeanMs,9:F2} {stats.P50Ms,9:F2} {stats.P95Ms,9:F2} "
            + $"{stats.P99Ms,9:F2} {stats.MaxMs,9:F2} "
            + $"{result.RequestsPerSecond,9:F1} {result.AllocatedBytesPerAttempt,11:F0}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project benchmarks\Mvk.Benchmarks -- [options]

            Options:
              --hosts <list>       comma-separated hosts (default: 10.20.0.203..210)
              --iterations <n>     measured iterations per operation (default: 20)
              --warmup <n>         warmup iterations per operation (default: 3)
              --delay-ms <n>       delay between measured iterations (default: 5)
              --channel <n>        channel 1..8 (default: 1)
              --measurement <n>    parameter number 1..184 (default: 55)
              --no-parallel        skip the parallel multi-device stage
              --help               show this help

            Benchmark is read-only: configuration write is disabled.
            """);
    }

    private sealed record BenchmarkOptions(
        IReadOnlyList<string> Hosts,
        int Iterations,
        int Warmup,
        int DelayMs,
        int Channel,
        int Measurement,
        bool RunParallel,
        bool ShowHelp)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            IReadOnlyList<string> hosts = Enumerable
                .Range(203, 8)
                .Select(lastOctet => $"10.20.0.{lastOctet}")
                .ToList();

            int iterations = DefaultIterations;
            int warmup = DefaultWarmup;
            int delayMs = DefaultDelayMs;
            int channel = DefaultChannel;
            int measurement = DefaultMeasurement;
            bool runParallel = true;
            bool showHelp = false;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--hosts":
                        hosts = ParseHosts(RequireValue(args, ref index));
                        break;

                    case "--iterations":
                        iterations = ParseInt(RequireValue(args, ref index), "--iterations", 1, 100_000);
                        break;

                    case "--warmup":
                        warmup = ParseInt(RequireValue(args, ref index), "--warmup", 0, 10_000);
                        break;

                    case "--delay-ms":
                        delayMs = ParseInt(RequireValue(args, ref index), "--delay-ms", 0, 60_000);
                        break;

                    case "--channel":
                        channel = ParseInt(RequireValue(args, ref index), "--channel", 1, Mvk01RegisterMap.MaxChannels);
                        break;

                    case "--measurement":
                        measurement = ParseInt(
                            RequireValue(args, ref index),
                            "--measurement",
                            1,
                            Mvk01RegisterMap.MaxMeasurementNumber);
                        break;

                    case "--no-parallel":
                        runParallel = false;
                        break;

                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;

                    default:
                        throw new ArgumentException($"Неизвестный аргумент: {args[index]}");
                }
            }

            return new BenchmarkOptions(
                hosts,
                iterations,
                warmup,
                delayMs,
                channel,
                measurement,
                runParallel,
                showHelp);
        }

        private static IReadOnlyList<string> ParseHosts(string value)
        {
            string[] hosts = value.Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (hosts.Length == 0)
            {
                throw new ArgumentException("Список --hosts пуст.");
            }

            return hosts;
        }

        private static string RequireValue(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Для {args[index]} не задано значение.");
            }

            index++;
            return args[index];
        }

        private static int ParseInt(string value, string option, int min, int max)
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result)
                || result < min
                || result > max)
            {
                throw new ArgumentException($"{option}: ожидается число {min}..{max}, получено '{value}'.");
            }

            return result;
        }
    }

    private sealed record BenchmarkCase(
        string Name,
        int RequestsPerOperation,
        Func<Mvk01Connection, CancellationToken, Task> Operation);

    private sealed record BenchmarkResult(
        string Device,
        string Name,
        int RequestsPerOperation,
        IReadOnlyList<double> Latencies,
        int Attempts,
        int Errors,
        long AllocatedBytes)
    {
        public LatencyStats Stats { get; } = LatencyStats.From(Latencies);

        public double RequestsPerSecond =>
            Stats.MeanMs > 0 ? 1000.0 / Stats.MeanMs * RequestsPerOperation : 0;

        public double AllocatedBytesPerAttempt =>
            Attempts > 0 ? AllocatedBytes / (double)Attempts : 0;
    }

    private sealed record LatencyStats(
        int Count,
        double MeanMs,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MaxMs)
    {
        public static LatencyStats From(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return new LatencyStats(0, 0, 0, 0, 0, 0);
            }

            double[] sorted = values.OrderBy(value => value).ToArray();
            double mean = values.Average();

            return new LatencyStats(
                values.Count,
                mean,
                Percentile(sorted, 50),
                Percentile(sorted, 95),
                Percentile(sorted, 99),
                sorted[^1]);
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            int index = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }
    }
}
