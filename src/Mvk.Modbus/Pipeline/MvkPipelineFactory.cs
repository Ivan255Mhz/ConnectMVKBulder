using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mvk.Modbus.Pipeline.Middlewares;

namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Создаёт наборы middleware по умолчанию.
    /// </summary>
    public static class MvkPipelineFactory
    {
        /// <summary>
        /// Создаёт middleware в порядке по умолчанию:
        /// timeout → logging → metrics → validation → retry → writeProtection.
        /// </summary>
        /// <param name="loggerFactory">Фабрика логгеров (необязательна).</param>
        public static IReadOnlyList<IModbusMiddleware> CreateDefaultMiddlewares(ILoggerFactory? loggerFactory = null)
        {
            ILoggerFactory factory = loggerFactory ?? NullLoggerFactory.Instance;

            return
            [
                new TimeoutMiddleware(),
                new LoggingMiddleware(factory.CreateLogger<LoggingMiddleware>()),
                new MetricsMiddleware(),
                new ValidationMiddleware(),
                new RetryMiddleware(),
                new WriteProtectionMiddleware()
            ];
        }
    }
}
