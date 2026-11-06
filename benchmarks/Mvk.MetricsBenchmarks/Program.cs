using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;
using Mvk.Modbus.Protocol;

namespace Mvk.MetricsBenchmarks;

internal static class Program
{
    private const int DefaultIterations = 200_000;
    private const int DefaultWarmup = 20_000;
    private const int DefaultRounds = 5;
    private static readonly ushort[] ResponseData = new ushort[16];

    private static readonly InvokeModbusDelegate Terminal = (context, _) =>
    {
        context.Response = ModbusResponse.Success(ResponseData);
        return Task.CompletedTask;
    };

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

        MvkDeviceSettings settings = CreateSettings();
        ModbusRequest request = ModbusRequest.ReadHoldingRegisters("bench", 0x4000, 16);
        IReadOnlyList<PipelineCase> cases = CreateCases();

        Console.WriteLine("MVK Modbus pipeline metrics benchmark");
        Console.WriteLine(
            $"Iterations: {options.Iterations}, warmup: {options.Warmup}, "
            + $"rounds: {options.Rounds} (best round)");
        Console.WriteLine();

        List<CaseResult> results = [];

        foreach (PipelineCase pipelineCase in cases)
        {
            CaseResult result = await RunCaseAsync(
                pipelineCase,
                settings,
                request,
                options);

            results.Add(result);
        }

        Console.WriteLine(
            $"{"Case",-28} {"ops/s",12} {"us/op",10} {"alloc/op",12} {"metrics",10}");
        Console.WriteLine(new string('-', 78));

        foreach (CaseResult result in results)
        {
            Console.WriteLine(
                $"{result.Name,-28} {result.OpsPerSecond,12:F0} "
                + $"{result.MicrosecondsPerOperation,10:F3} "
                + $"{result.AllocatedBytesPerOperation,12:F0} "
                + $"{(result.MetricsMeasurements >= 0 ? result.MetricsMeasurements.ToString("N0", CultureInfo.InvariantCulture) : "-"),10}");
        }

        CaseResult baseline = results[0];
        CaseResult metrics = results.First(result => result.Name == "metrics (no listener)");
        CaseResult metricsListener = results.First(result => result.Name == "metrics + listener");

        Console.WriteLine();
        Console.WriteLine(
            $"Metrics overhead: +{(metrics.MicrosecondsPerOperation - baseline.MicrosecondsPerOperation) * 1000:F0} ns/op "
            + $"(without listener), "
            + $"+{(metricsListener.MicrosecondsPerOperation - baseline.MicrosecondsPerOperation) * 1000:F0} ns/op "
            + $"(with listener).");

        Console.WriteLine(
            $"Metrics listener events: {metricsListener.MetricsMeasurements:N0} "
            + $"for {options.Iterations:N0} operations.");

        return 0;
    }

    private static IReadOnlyList<PipelineCase> CreateCases()
    {
        return
        [
            new("no middleware", new MvkPipeline(Terminal, []), ListenToMetrics: false),
            new("validation", new MvkPipeline(
                Terminal,
                [new ValidationMiddleware()]),
                ListenToMetrics: false),
            new("metrics (no listener)", new MvkPipeline(
                Terminal,
                [new MetricsMiddleware()]),
                ListenToMetrics: false),
            new("metrics + listener", new MvkPipeline(
                Terminal,
                [new MetricsMiddleware()]),
                ListenToMetrics: true),
            new("logging (null)", new MvkPipeline(
                Terminal,
                [new LoggingMiddleware(NullLogger<LoggingMiddleware>.Instance)]),
                ListenToMetrics: false),
            new("logging (enabled no-op)", new MvkPipeline(
                Terminal,
                [new LoggingMiddleware(new EnabledNoOpLogger<LoggingMiddleware>())]),
                ListenToMetrics: false),
            new("default middleware", new MvkPipeline(
                Terminal,
                MvkPipelineFactory.CreateDefaultMiddlewares()),
                ListenToMetrics: false)
        ];
    }

    private static async Task<CaseResult> RunCaseAsync(
        PipelineCase pipelineCase,
        MvkDeviceSettings settings,
        ModbusRequest request,
        BenchmarkOptions options)
    {
        MeterListener? listener = null;
        long metricsMeasurements = 0;

        if (pipelineCase.ListenToMetrics)
        {
            listener = CreateMetricsListener(() => metricsMeasurements++);
            listener.Start();
        }

        try
        {
            for (int warmup = 0; warmup < options.Warmup; warmup++)
            {
                await RunOnceAsync(pipelineCase.Pipeline, settings, request);
            }

            List<CaseResult> rounds = new(options.Rounds);

            for (int round = 0; round < options.Rounds; round++)
            {
                metricsMeasurements = 0;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                long allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                long startedAt = Stopwatch.GetTimestamp();

                for (int iteration = 0; iteration < options.Iterations; iteration++)
                {
                    await RunOnceAsync(pipelineCase.Pipeline, settings, request);
                }

                long finishedAt = Stopwatch.GetTimestamp();
                long allocatedBytes = Math.Max(
                    0,
                    GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore);

                rounds.Add(new CaseResult(
                    pipelineCase.Name,
                    options.Iterations,
                    Stopwatch.GetElapsedTime(startedAt, finishedAt).TotalMilliseconds,
                    allocatedBytes,
                    pipelineCase.ListenToMetrics ? metricsMeasurements : -1));
            }

            return rounds.OrderBy(result => result.ElapsedMs).First();
        }
        finally
        {
            listener?.Dispose();
        }
    }

    private static async Task RunOnceAsync(
        MvkPipeline pipeline,
        MvkDeviceSettings settings,
        ModbusRequest request)
    {
        ModbusRequestContext context = new(settings, request, CancellationToken.None);
        await pipeline.ExecuteAsync(context);
    }

    private static MeterListener CreateMetricsListener(Action onMeasurement)
    {
        MeterListener listener = new();

        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "Mvk.Modbus")
            {
                current.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((_, _, _, _) => onMeasurement());
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => onMeasurement());

        return listener;
    }

    private static MvkDeviceSettings CreateSettings()
    {
        return new MvkDeviceSettings
        {
            Id = "bench",
            Name = "bench",
            Host = "127.0.0.1",
            Port = 502,
            SlaveId = 1,
            ByteOrder = MvkByteOrder.BigEndian,
            Enabled = true,
            MaxConcurrentRequests = 1,
            AllowConfigurationWrite = false,
            ConnectTimeoutMs = 1000,
            ReadTimeoutMs = 1000,
            WriteTimeoutMs = 1000,
            MaxRetries = 0,
            RetryDelayMs = 0,
            ReconnectBaseDelayMs = 500,
            ReconnectMaxDelayMs = 30_000,
            IdleDisconnectSeconds = 0,
            WritePolicy = MvkWritePolicy.ProtectConfigArea,
            ProtectedWriteStartAddress = 0,
            ProtectedWriteEndAddress = 0x0FFF,
            RequireEvenAddressAndQuantity = false,
            Tags = new Dictionary<string, string>()
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project benchmarks\Mvk.MetricsBenchmarks -- [options]

            Options:
              --iterations <n>     measured operations per case (default: 200000)
              --warmup <n>         warmup operations per case (default: 20000)
              --rounds <n>         measured rounds per case, best is reported (default: 5)
              --help               show this help

            In-process benchmark: no network and no real devices.
            """);
    }

    private sealed record BenchmarkOptions(
        int Iterations,
        int Warmup,
        int Rounds,
        bool ShowHelp)
    {
        public static BenchmarkOptions Parse(string[] args)
        {
            int iterations = DefaultIterations;
            int warmup = DefaultWarmup;
            int rounds = DefaultRounds;
            bool showHelp = false;

            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--iterations":
                        iterations = ParseInt(
                            RequireValue(args, ref index),
                            "--iterations",
                            1,
                            10_000_000);
                        break;

                    case "--warmup":
                        warmup = ParseInt(
                            RequireValue(args, ref index),
                            "--warmup",
                            0,
                            1_000_000);
                        break;

                    case "--rounds":
                        rounds = ParseInt(
                            RequireValue(args, ref index),
                            "--rounds",
                            1,
                            20);
                        break;

                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;

                    default:
                        throw new ArgumentException($"Неизвестный аргумент: {args[index]}");
                }
            }

            return new BenchmarkOptions(iterations, warmup, rounds, showHelp);
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

    private sealed record PipelineCase(
        string Name,
        MvkPipeline Pipeline,
        bool ListenToMetrics);

    private sealed record CaseResult(
        string Name,
        int Iterations,
        double ElapsedMs,
        long AllocatedBytes,
        long MetricsMeasurements)
    {
        public double MicrosecondsPerOperation =>
            Iterations > 0 ? ElapsedMs * 1000.0 / Iterations : 0;

        public double OpsPerSecond =>
            ElapsedMs > 0 ? Iterations / (ElapsedMs / 1000.0) : 0;

        public double AllocatedBytesPerOperation =>
            Iterations > 0 ? AllocatedBytes / (double)Iterations : 0;
    }

    private sealed class EnabledNoOpLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
