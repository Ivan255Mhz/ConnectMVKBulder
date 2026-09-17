using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pipeline.Middlewares;

namespace Mvk.Modbus.DependencyInjection
{
    /// <summary>
    /// Создаёт список middleware по именам из секции <c>Pipeline</c>:
    /// встроенные имена и пользовательские из <see cref="MvkMiddlewareRegistry"/>.
    /// </summary>
    public static class MvkMiddlewareResolver
    {
        /// <summary>
        /// Разрешает middleware в порядке, заданном конфигурацией.
        /// </summary>
        /// <param name="options">Корневая конфигурация библиотеки.</param>
        /// <param name="serviceProvider">Провайдер служб для создания middleware.</param>
        /// <exception cref="MvkModbusException">
        /// Имя middleware не найдено (<see cref="MvkErrorKind.Configuration"/>).
        /// </exception>
        public static IReadOnlyList<IModbusMiddleware> ResolveMiddlewares(
            MvkModbusOptions options,
            IServiceProvider serviceProvider)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(serviceProvider);

            MvkMiddlewareRegistry? registry = serviceProvider.GetService<MvkMiddlewareRegistry>();
            List<IModbusMiddleware> middlewares = [];

            foreach (string name in options.ResolvePipeline())
            {
                if (TryCreateBuiltIn(name, serviceProvider, out IModbusMiddleware? builtIn))
                {
                    middlewares.Add(builtIn);
                    continue;
                }

                if (registry is not null && registry.TryGetType(name, out Type? customType))
                {
                    middlewares.Add((IModbusMiddleware)ActivatorUtilities.CreateInstance(serviceProvider, customType));
                    continue;
                }

                throw new MvkModbusException(
                    $"Middleware '{name}' не найден: нет встроенного имени и нет регистрации "
                    + "через AddModbusMiddleware<T>(services, \"имя\").",
                    MvkErrorKind.Configuration);
            }

            return middlewares;
        }

        private static bool TryCreateBuiltIn(
            string name,
            IServiceProvider serviceProvider,
            out IModbusMiddleware middleware)
        {
            string key = name.Trim();

            if (IsName(key, MvkMiddlewareNames.Timeout))
            {
                middleware = new TimeoutMiddleware();
            }
            else if (IsName(key, MvkMiddlewareNames.Logging))
            {
                middleware = new LoggingMiddleware(GetLogger<LoggingMiddleware>(serviceProvider));
            }
            else if (IsName(key, MvkMiddlewareNames.Metrics))
            {
                middleware = new MetricsMiddleware();
            }
            else if (IsName(key, MvkMiddlewareNames.Validation))
            {
                middleware = new ValidationMiddleware();
            }
            else if (IsName(key, MvkMiddlewareNames.Retry))
            {
                middleware = new RetryMiddleware();
            }
            else if (IsName(key, MvkMiddlewareNames.WriteProtection))
            {
                middleware = new WriteProtectionMiddleware();
            }
            else
            {
                middleware = null!;
                return false;
            }

            return true;
        }

        private static bool IsName(string name, string expected)
        {
            return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static ILogger<T>? GetLogger<T>(IServiceProvider serviceProvider)
        {
            return serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<T>();
        }
    }
}
