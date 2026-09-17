namespace Mvk.Modbus.Pipeline
{
    /// <summary>
    /// Имена встроенных middleware для секции <c>Pipeline</c> конфигурации.
    /// </summary>
    public static class MvkMiddlewareNames
    {
        /// <summary>
        /// Ограничение общего времени операции (<see cref="Middlewares.TimeoutMiddleware"/>).
        /// </summary>
        public const string Timeout = "timeout";

        /// <summary>
        /// Логирование запросов (<see cref="Middlewares.LoggingMiddleware"/>).
        /// </summary>
        public const string Logging = "logging";

        /// <summary>
        /// Проверка запроса (<see cref="Middlewares.ValidationMiddleware"/>).
        /// </summary>
        public const string Validation = "validation";

        /// <summary>
        /// Повторные попытки при временных ошибках (<see cref="Middlewares.RetryMiddleware"/>).
        /// </summary>
        public const string Retry = "retry";

        /// <summary>
        /// Метрики (<see cref="Middlewares.MetricsMiddleware"/>).
        /// </summary>
        public const string Metrics = "metrics";

        /// <summary>
        /// Защита от записи (<see cref="Middlewares.WriteProtectionMiddleware"/>).
        /// </summary>
        public const string WriteProtection = "writeProtection";
    }
}
