using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pipeline.Middlewares
{
    /// <summary>
    /// Повторяет запрос при временных ошибках (<see cref="MvkModbusException.IsTransient"/>):
    /// число попыток и задержка берутся из параметров устройства. Задержка растёт линейно.
    /// </summary>
    public sealed class RetryMiddleware : IModbusMiddleware
    {
        /// <inheritdoc/>
        public async Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
        {
            MvkDeviceSettings settings = context.Settings;
            int maxAttempts = Math.Max(1, settings.MaxRetries + 1);

            for (int attempt = 1; ; attempt++)
            {
                context.Attempt = attempt;
                context.Response = null;

                await next(context, cancellationToken).ConfigureAwait(false);

                ModbusResponse? response = context.Response;
                if (response is null || response.IsSuccess)
                {
                    return;
                }

                if (response.Error is null || !response.Error.IsTransient || attempt >= maxAttempts)
                {
                    return;
                }

                int delayMs = settings.RetryDelayMs * attempt;
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }
}
