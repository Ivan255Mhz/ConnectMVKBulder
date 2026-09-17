using System.Collections.Generic;

namespace Mvk.MVK01
{
    /// <summary>
    /// Фабрика типизированных устройств МВК01 поверх пула.
    /// </summary>
    public interface IMvk01DeviceFactory
    {
        /// <summary>
        /// Возвращает типизированное устройство по идентификатору из конфигурации.
        /// </summary>
        /// <param name="id">Идентификатор устройства.</param>
        /// <exception cref="Modbus.MvkModbusException">
        /// Устройство не найдено (<see cref="Modbus.MvkErrorKind.DeviceNotFound"/>).
        /// </exception>
        IMvk01Device GetDevice(string id);

        /// <summary>
        /// Пробует получить типизированное устройство по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор устройства.</param>
        /// <param name="device">Найденное устройство.</param>
        bool TryGetDevice(string id, out IMvk01Device device);

        /// <summary>
        /// Все устройства пула в типизированном виде.
        /// </summary>
        IReadOnlyCollection<IMvk01Device> Devices { get; }
    }
}
