using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Сборщик пайплайна middleware.
    /// </summary>
    public sealed class MvkPipelineBuilder
    {
        private readonly List<IModbusMiddleware> middlewares = [];

        /// <summary>
        /// Зарегистрированные middleware в порядке выполнения (первый — самый внешний).
        /// </summary>
        public IReadOnlyList<IModbusMiddleware> Middlewares => middlewares;

        /// <summary>
        /// Добавляет middleware в конец пайплайна.
        /// </summary>
        /// <param name="middleware">Экземпляр middleware.</param>
        public MvkPipelineBuilder Use(IModbusMiddleware middleware)
        {
            ArgumentNullException.ThrowIfNull(middleware);
            middlewares.Add(middleware);
            return this;
        }

        /// <summary>
        /// Добавляет middleware в виде функции.
        /// </summary>
        /// <param name="middleware">Функция middleware.</param>
        public MvkPipelineBuilder Use(Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task> middleware)
        {
            ArgumentNullException.ThrowIfNull(middleware);
            return Use(new DelegateMiddleware(middleware));
        }

        /// <summary>
        /// Собирает пайплайн с указанным терминальным обработчиком (обычно транспорт).
        /// </summary>
        /// <param name="terminal">Терминальный обработчик, выполняющий запрос.</param>
        public MvkPipeline Build(InvokeModbusDelegate terminal)
        {
            ArgumentNullException.ThrowIfNull(terminal);
            return new MvkPipeline(terminal, middlewares);
        }

        private sealed class DelegateMiddleware : IModbusMiddleware
        {
            private readonly Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task> middleware;

            public DelegateMiddleware(Func<ModbusRequestContext, InvokeModbusDelegate, CancellationToken, Task> middleware)
            {
                this.middleware = middleware;
            }

            public Task InvokeAsync(ModbusRequestContext context, InvokeModbusDelegate next, CancellationToken cancellationToken)
            {
                return middleware(context, next, cancellationToken);
            }
        }
    }
}
