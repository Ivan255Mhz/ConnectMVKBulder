using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;

namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Пул устройств: хранение параметров, доступ по идентификатору, фоновый реконнект
    /// и отключение простаивающих соединений.
    /// </summary>
    public interface IMvkDevicePool : IAsyncDisposable
    {
        /// <summary>
        /// Корневая конфигурация пула.
        /// </summary>
        MvkModbusOptions Options { get; }

        /// <summary>
        /// Устройства пула.
        /// </summary>
        IReadOnlyCollection<IMvkDevice> Devices { get; }

        /// <summary>
        /// Событие изменения состояния любого устройства пула.
        /// </summary>
        event EventHandler<MvkDeviceStateChangedEventArgs>? DeviceStateChanged;

        /// <summary>
        /// Возвращает устройство по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор устройства из конфигурации.</param>
        /// <exception cref="MvkModbusException">
        /// Устройство не найдено (<see cref="MvkErrorKind.DeviceNotFound"/>).
        /// </exception>
        IMvkDevice GetDevice(string id);

        /// <summary>
        /// Пробует получить устройство по идентификатору.
        /// </summary>
        /// <param name="id">Идентификатор устройства из конфигурации.</param>
        /// <param name="device">Найденное устройство.</param>
        bool TryGetDevice(string id, out IMvkDevice device);

        /// <summary>
        /// Добавляет устройство в пул во время работы.
        /// </summary>
        /// <param name="deviceOptions">Параметры устройства.</param>
        /// <exception cref="MvkModbusException">
        /// Устройство с таким идентификатором уже существует (<see cref="MvkErrorKind.Configuration"/>).
        /// </exception>
        IMvkDevice AddDevice(MvkDeviceOptions deviceOptions);

        /// <summary>
        /// Удаляет устройство из пула и освобождает его ресурсы.
        /// </summary>
        /// <param name="id">Идентификатор устройства.</param>
        bool RemoveDevice(string id);

        /// <summary>
        /// Выполняет один цикл обслуживания: реконнект Faulted-устройств и отключение простаивающих.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task MaintainAsync(CancellationToken cancellationToken = default);
    }
}
