using System.Collections.Generic;

namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Неизменяемые параметры устройства после применения настроек по умолчанию.
    /// </summary>
    public sealed record MvkDeviceSettings
    {
        /// <summary>
        /// Уникальный идентификатор устройства в пуле.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>
        /// Отображаемое имя устройства.
        /// </summary>
        public string? Name { get; init; }

        /// <summary>
        /// Адрес устройства (IP или DNS-имя).
        /// </summary>
        public required string Host { get; init; }

        /// <summary>
        /// TCP-порт Modbus.
        /// </summary>
        public required int Port { get; init; }

        /// <summary>
        /// Идентификатор устройства Modbus.
        /// </summary>
        public required byte SlaveId { get; init; }

        /// <summary>
        /// Порядок передачи байт для 32-битных значений.
        /// </summary>
        public required MvkByteOrder ByteOrder { get; init; }

        /// <summary>
        /// Устройство включено в работу.
        /// </summary>
        public required bool Enabled { get; init; }

        /// <summary>
        /// Максимальное число одновременных запросов к устройству.
        /// </summary>
        public required int MaxConcurrentRequests { get; init; }

        /// <summary>
        /// Запись в защищённую область конфигурации разрешена.
        /// </summary>
        public required bool AllowConfigurationWrite { get; init; }

        /// <summary>
        /// Таймаут подключения, мс.
        /// </summary>
        public required int ConnectTimeoutMs { get; init; }

        /// <summary>
        /// Таймаут чтения, мс.
        /// </summary>
        public required int ReadTimeoutMs { get; init; }

        /// <summary>
        /// Таймаут записи, мс.
        /// </summary>
        public required int WriteTimeoutMs { get; init; }

        /// <summary>
        /// Число повторных попыток при временных ошибках.
        /// </summary>
        public required int MaxRetries { get; init; }

        /// <summary>
        /// Базовая задержка между повторными попытками, мс.
        /// </summary>
        public required int RetryDelayMs { get; init; }

        /// <summary>
        /// Базовая задержка реконнекта, мс.
        /// </summary>
        public required int ReconnectBaseDelayMs { get; init; }

        /// <summary>
        /// Максимальная задержка реконнекта, мс.
        /// </summary>
        public required int ReconnectMaxDelayMs { get; init; }

        /// <summary>
        /// Время простоя, после которого соединение закрывается, с. 0 — не закрывать.
        /// </summary>
        public required int IdleDisconnectSeconds { get; init; }

        /// <summary>
        /// Политика записи в регистры.
        /// </summary>
        public required MvkWritePolicy WritePolicy { get; init; }

        /// <summary>
        /// Начало защищённой области конфигурации (включительно).
        /// </summary>
        public required ushort ProtectedWriteStartAddress { get; init; }

        /// <summary>
        /// Конец защищённой области конфигурации (включительно).
        /// </summary>
        public required ushort ProtectedWriteEndAddress { get; init; }

        /// <summary>
        /// Требовать чётные адрес и число регистров (особенность МВК01).
        /// </summary>
        public required bool RequireEvenAddressAndQuantity { get; init; }

        /// <summary>
        /// Произвольные метки устройства.
        /// </summary>
        public required IReadOnlyDictionary<string, string> Tags { get; init; }

        /// <summary>
        /// Адрес устройства в формате «host:порт».
        /// </summary>
        public string Endpoint => $"{Host}:{Port}";
    }
}
