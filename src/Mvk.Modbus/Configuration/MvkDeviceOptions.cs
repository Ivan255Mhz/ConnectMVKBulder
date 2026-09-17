using System;
using System.Collections.Generic;

namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Настройки одного устройства из конфигурации. Незаданные значения наследуются
    /// из <see cref="MvkDefaultsOptions"/>.
    /// </summary>
    public sealed class MvkDeviceOptions
    {
        /// <summary>
        /// Уникальный идентификатор устройства в пуле (обязателен).
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Отображаемое имя устройства.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Адрес устройства (IP или DNS-имя, обязателен).
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// TCP-порт Modbus. <see langword="null"/> — взять из настроек по умолчанию.
        /// </summary>
        public int? Port { get; set; }

        /// <summary>
        /// Идентификатор устройства Modbus. <see langword="null"/> — взять из настроек по умолчанию.
        /// </summary>
        public byte? SlaveId { get; set; }

        /// <summary>
        /// Порядок передачи байт. <see langword="null"/> — взять из настроек по умолчанию.
        /// </summary>
        public MvkByteOrder? ByteOrder { get; set; }

        /// <summary>
        /// Устройство включено в работу. По умолчанию <see langword="true"/>.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Максимальное число одновременных запросов к устройству.
        /// <see langword="null"/> — взять из настроек по умолчанию.
        /// </summary>
        public int? MaxConcurrentRequests { get; set; }

        /// <summary>
        /// Разрешить запись в защищённую область конфигурации.
        /// <see langword="null"/> — взять из настроек по умолчанию.
        /// </summary>
        public bool? AllowConfigurationWrite { get; set; }

        /// <summary>
        /// Произвольные метки устройства (расположение, агрегат и т.п.).
        /// </summary>
        public Dictionary<string, string>? Tags { get; set; }

        /// <summary>
        /// Применяет настройки по умолчанию и возвращает неизменяемый набор параметров устройства.
        /// </summary>
        /// <param name="defaults">Настройки по умолчанию для пула.</param>
        public MvkDeviceSettings Resolve(MvkDefaultsOptions defaults)
        {
            ArgumentNullException.ThrowIfNull(defaults);

            Dictionary<string, string> tags = Tags is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(Tags, StringComparer.OrdinalIgnoreCase);

            return new MvkDeviceSettings
            {
                Id = Id?.Trim() ?? string.Empty,
                Name = Name,
                Host = Host?.Trim() ?? string.Empty,
                Port = Port ?? defaults.Port,
                SlaveId = SlaveId ?? defaults.SlaveId,
                ByteOrder = ByteOrder ?? defaults.ByteOrder,
                Enabled = Enabled,
                MaxConcurrentRequests = MaxConcurrentRequests ?? defaults.MaxConcurrentRequests,
                AllowConfigurationWrite = AllowConfigurationWrite ?? defaults.AllowConfigurationWrite,
                ConnectTimeoutMs = defaults.ConnectTimeoutMs,
                ReadTimeoutMs = defaults.ReadTimeoutMs,
                WriteTimeoutMs = defaults.WriteTimeoutMs,
                MaxRetries = defaults.MaxRetries,
                RetryDelayMs = defaults.RetryDelayMs,
                ReconnectBaseDelayMs = defaults.ReconnectBaseDelayMs,
                ReconnectMaxDelayMs = defaults.ReconnectMaxDelayMs,
                IdleDisconnectSeconds = defaults.IdleDisconnectSeconds,
                WritePolicy = defaults.WritePolicy,
                ProtectedWriteStartAddress = defaults.ProtectedWriteStartAddress,
                ProtectedWriteEndAddress = defaults.ProtectedWriteEndAddress,
                Tags = tags
            };
        }
    }
}
