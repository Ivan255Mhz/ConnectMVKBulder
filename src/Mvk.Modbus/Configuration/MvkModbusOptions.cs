using System.Collections.Generic;
using System.Linq;

namespace Mvk.Modbus.Configuration
{
    /// <summary>
    /// Корневая секция конфигурации библиотеки (<c>MvkModbus</c>).
    /// </summary>
    public sealed class MvkModbusOptions
    {
        /// <summary>
        /// Настройки по умолчанию для всех устройств.
        /// </summary>
        public MvkDefaultsOptions Defaults { get; set; } = new();

        /// <summary>
        /// Настройки пула устройств.
        /// </summary>
        public MvkPoolOptions Pool { get; set; } = new();

        /// <summary>
        /// Порядок middleware пайплайна (первый — самый внешний).
        /// <see langword="null"/> — использовать порядок по умолчанию.
        /// </summary>
        public List<string>? Pipeline { get; set; }

        /// <summary>
        /// Список устройств пула.
        /// </summary>
        public List<MvkDeviceOptions>? Devices { get; set; }

        /// <summary>
        /// Возвращает порядок middleware: из конфигурации либо по умолчанию.
        /// </summary>
        public IReadOnlyList<string> ResolvePipeline()
        {
            return Pipeline ?? CreateDefaultPipeline();
        }

        /// <summary>
        /// Применяет настройки по умолчанию и возвращает параметры всех устройств пула.
        /// </summary>
        public IReadOnlyList<MvkDeviceSettings> ResolveDevices()
        {
            if (Devices is null)
            {
                return [];
            }

            return Devices.Select(device => device.Resolve(Defaults)).ToList();
        }

        /// <summary>
        /// Порядок middleware по умолчанию: timeout → logging → validation → retry → writeProtection.
        /// </summary>
        public static List<string> CreateDefaultPipeline()
        {
            return ["timeout", "logging", "validation", "retry", "writeProtection"];
        }
    }
}
