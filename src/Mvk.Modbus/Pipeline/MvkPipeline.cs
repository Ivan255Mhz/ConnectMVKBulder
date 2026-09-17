using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Собранный пайплайн обработки запросов: middleware + терминальный обработчик.
    /// </summary>
    public sealed class MvkPipeline
    {
        private readonly InvokeModbusDelegate head;

        /// <summary>
        /// Создаёт пайплайн.
        /// </summary>
        /// <param name="terminal">Терминальный обработчик, выполняющий запрос.</param>
        /// <param name="middlewares">Middleware в порядке выполнения (первый — самый внешний).</param>
        public MvkPipeline(InvokeModbusDelegate terminal, IEnumerable<IModbusMiddleware> middlewares)
        {
            ArgumentNullException.ThrowIfNull(terminal);
            ArgumentNullException.ThrowIfNull(middlewares);

            InvokeModbusDelegate next = terminal;
            foreach (IModbusMiddleware middleware in middlewares.Reverse())
            {
                InvokeModbusDelegate downstream = next;
                IModbusMiddleware current = middleware;
                next = (context, cancellationToken) => current.InvokeAsync(context, downstream, cancellationToken);
            }

            head = next;
        }

        /// <summary>
        /// Терминальный обработчик, вызывающий транспорт устройства.
        /// </summary>
        /// <param name="transport">Транспорт устройства.</param>
        public static InvokeModbusDelegate TerminalFromTransport(IMvkTransport transport)
        {
            ArgumentNullException.ThrowIfNull(transport);

            return async (context, cancellationToken) =>
            {
                context.Response = await transport
                    .ExecuteAsync(context.Request, cancellationToken)
                    .ConfigureAwait(false);
            };
        }

        /// <summary>
        /// Выполняет запрос через пайплайн и возвращает ответ.
        /// </summary>
        /// <param name="context">Контекст запроса.</param>
        /// <exception cref="MvkModbusException">
        /// Пайплайн завершился без ответа (<see cref="MvkErrorKind.Unknown"/>).
        /// </exception>
        public async Task<ModbusResponse> ExecuteAsync(ModbusRequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            await head(context, context.Aborted).ConfigureAwait(false);

            return context.Response ?? throw new MvkModbusException(
                "Пайплайн завершился без ответа: терминальный обработчик не был вызван.",
                MvkErrorKind.Unknown,
                context.Settings.Id);
        }
    }
}
