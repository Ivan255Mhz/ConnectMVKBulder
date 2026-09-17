using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Ограничивает общее время операции. Таймаут задаётся в
    /// <see cref="ModbusRequestContext.Items"/> по ключу <see cref="MvkContextItems.TimeoutMs"/>;
    /// если он не задан, middleware не вмешивается (действуют таймауты сокета транспорта).
    /// </summary>
    public sealed class TimeoutMiddleware : IModbusMiddleware
    {
        /// <inheritdoc/>
        public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            if (!context.TryGetItem<int>(MvkContextItems.TimeoutMs, out int timeoutMs) || timeoutMs <= 0)
            {
                await next(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeoutMs);

            try
            {
                await next(context, timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                context.Response = ModbusResponse.Failure(new MvkModbusException(
                    $"Таймаут операции {context.Request.Operation} (0x{context.Request.StartAddress:X4}) — превышено {timeoutMs} мс.",
                    MvkErrorKind.Timeout,
                    context.Settings.Id));
            }
        }
    }
}
