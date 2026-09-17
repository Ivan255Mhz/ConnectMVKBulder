using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Публикует метрики запросов через <see cref="System.Diagnostics.Metrics"/>:
    /// счётчик <c>mvk.modbus.requests</c> и гистограмму <c>mvk.modbus.duration</c> (мс)
    /// с тегами device / operation / result.
    /// </summary>
    public sealed class MetricsMiddleware : IModbusMiddleware
    {
        private static readonly Meter Meter = new("Mvk.Modbus", "1.0.0");

        private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
            "mvk.modbus.requests",
            description: "Количество запросов к устройствам МВК.");

        private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
            "mvk.modbus.duration",
            unit: "ms",
            description: "Длительность запросов к устройствам МВК.");

        /// <inheritdoc/>
        public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            long startedAt = Stopwatch.GetTimestamp();

            await next(context, cancellationToken).ConfigureAwait(false);

            TagList tags = new()
            {
                { "device", context.Settings.Id },
                { "operation", context.Request.Operation.ToString() },
                { "result", context.Response?.IsSuccess == true ? "success" : "error" }
            };

            Requests.Add(1, tags);
            Duration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, tags);
        }
    }
}
