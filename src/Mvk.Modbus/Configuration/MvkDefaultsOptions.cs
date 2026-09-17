namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Настройки по умолчанию для всех устройств пула. Могут переопределяться
    /// на уровне отдельного устройства (<see cref="MvkDeviceOptions"/>).
    /// </summary>
    public sealed class MvkDefaultsOptions
    {
        /// <summary>
        /// TCP-порт Modbus. По умолчанию 502.
        /// </summary>
        public int Port { get; set; } = 502;

        /// <summary>
        /// Идентификатор устройства Modbus (slave id). По умолчанию 1.
        /// </summary>
        public byte SlaveId { get; set; } = 1;

        /// <summary>
        /// Порядок передачи байт для 32-битных значений. По умолчанию <see cref="MvkByteOrder.BigEndian"/>.
        /// </summary>
        public MvkByteOrder ByteOrder { get; set; } = MvkByteOrder.BigEndian;

        /// <summary>
        /// Таймаут подключения, мс. По умолчанию 3000.
        /// </summary>
        public int ConnectTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// Таймаут чтения, мс. По умолчанию 1000.
        /// </summary>
        public int ReadTimeoutMs { get; set; } = 1000;

        /// <summary>
        /// Таймаут записи, мс. По умолчанию 3000.
        /// </summary>
        public int WriteTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// Число повторных попыток при временных ошибках. По умолчанию 2.
        /// </summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// Базовая задержка между повторными попытками, мс. По умолчанию 150.
        /// </summary>
        public int RetryDelayMs { get; set; } = 150;

        /// <summary>
        /// Базовая задержка реконнекта, мс. По умолчанию 500.
        /// </summary>
        public int ReconnectBaseDelayMs { get; set; } = 500;

        /// <summary>
        /// Максимальная задержка реконнекта (ограничение экспоненциального роста), мс. По умолчанию 30000.
        /// </summary>
        public int ReconnectMaxDelayMs { get; set; } = 30000;

        /// <summary>
        /// Время простоя, после которого соединение закрывается, с. 0 — не закрывать. По умолчанию 120.
        /// </summary>
        public int IdleDisconnectSeconds { get; set; } = 120;

        /// <summary>
        /// Максимальное число одновременных запросов к одному устройству. По умолчанию 1.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 1;

        /// <summary>
        /// Политика записи. По умолчанию <see cref="MvkWritePolicy.ProtectConfigArea"/>.
        /// </summary>
        public MvkWritePolicy WritePolicy { get; set; } = MvkWritePolicy.ProtectConfigArea;

        /// <summary>
        /// Разрешить запись в защищённую область конфигурации при политике
        /// <see cref="MvkWritePolicy.ProtectConfigArea"/>. По умолчанию запрещено.
        /// </summary>
        public bool AllowConfigurationWrite { get; set; }

        /// <summary>
        /// Начало защищённой области конфигурации (включительно). По умолчанию 0x0000.
        /// </summary>
        public ushort ProtectedWriteStartAddress { get; set; } = 0x0000;

        /// <summary>
        /// Конец защищённой области конфигурации (включительно). По умолчанию 0x0FFF.
        /// </summary>
        public ushort ProtectedWriteEndAddress { get; set; } = 0x0FFF;
    }
}
