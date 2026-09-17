using Mvk.Modbus.Configuration;

namespace Mvk.Modbus.Transport
{
    /// <summary>
    /// Фабрика транспортов: создаёт отдельный транспорт для каждого устройства пула.
    /// </summary>
    public interface IMvkTransportFactory
    {
        /// <summary>
        /// Создаёт транспорт для устройства.
        /// </summary>
        /// <param name="settings">Параметры устройства.</param>
        IMvkTransport Create(MvkDeviceSettings settings);
    }
}
