namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Настройки пула устройств.
    /// </summary>
    public sealed class MvkPoolOptions
    {
        /// <summary>
        /// Фоновое восстановление соединений и health-check. По умолчанию включено.
        /// </summary>
        public bool ReconnectEnabled { get; set; } = true;

        /// <summary>
        /// Период фоновой проверки устройств (реконнект, idle-отключение), с. По умолчанию 15.
        /// </summary>
        public int HealthCheckIntervalSeconds { get; set; } = 15;
    }
}
