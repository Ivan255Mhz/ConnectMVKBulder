using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Логирует каждый запрос: устройство, операцию, адрес, результат и длительность.
    /// Успешные запросы — уровень Debug, ошибки — Warning.
    /// </summary>
    public sealed class LoggingMiddleware : IModbusMiddleware
    {
        private readonly ILogger<LoggingMiddleware> logger;

        /// <summary>
        /// Создаёт middleware логирования.
        /// </summary>
        /// <param name="logger">Логгер (необязателен).</param>
        public LoggingMiddleware(ILogger<LoggingMiddleware>? logger = null)
        {
            this.logger = logger ?? NullLogger<LoggingMiddleware>.Instance;
        }

        /// <inheritdoc/>
        public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            long startedAt = Stopwatch.GetTimestamp();

            await next(context, cancellationToken).ConfigureAwait(false);

            double elapsedMs = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (context.Response?.IsSuccess == true)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "МВК '{DeviceId}': {Operation} 0x{Address:X4} x{Quantity} — успех за {ElapsedMs:F1} мс (попытка {Attempt}).",
                        context.Settings.Id,
                        context.Request.Operation,
                        context.Request.StartAddress,
                        context.Request.Quantity,
                        elapsedMs,
                        context.Attempt);
                }
            }
            else if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "МВК '{DeviceId}': {Operation} 0x{Address:X4} x{Quantity} — ошибка {ErrorKind} за {ElapsedMs:F1} мс (попытка {Attempt}): {Message}",
                    context.Settings.Id,
                    context.Request.Operation,
                    context.Request.StartAddress,
                    context.Request.Quantity,
                    context.Response?.Error?.ErrorKind,
                    elapsedMs,
                    context.Attempt,
                    context.Response?.Error?.Message);
            }
        }
    }
}
