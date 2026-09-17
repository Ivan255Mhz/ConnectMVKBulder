using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Pool
{
    /// <summary>
    /// Устройство пула: сериализует запросы и выполняет их через пайплайн middleware.
    /// Метод <see cref="ExecuteAsync"/> возвращает ответ (в том числе с ошибкой),
    /// удобные обёртки чтения/записи выбрасывают <see cref="MvkModbusException"/>.
    /// </summary>
    public interface IMvkDevice : IDisposable
    {
        /// <summary>
        /// Параметры устройства (с применёнными настройками по умолчанию).
        /// </summary>
        MvkDeviceSettings Settings { get; }

        /// <summary>
        /// Текущее состояние.
        /// </summary>
        MvkDeviceState State { get; }

        /// <summary>
        /// Соединение установлено.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Последняя ошибка обращения (сбрасывается при успехе).
        /// </summary>
        MvkModbusException? LastError { get; }

        /// <summary>
        /// Время последнего запроса к устройству (UTC).
        /// </summary>
        DateTimeOffset LastRequestAt { get; }

        /// <summary>
        /// Событие изменения состояния устройства.
        /// </summary>
        event EventHandler<MvkDeviceStateChangedEventArgs>? StateChanged;

        /// <summary>
        /// Выполняет запрос через пайплайн; при необходимости подключается автоматически.
        /// </summary>
        /// <param name="request">Запрос к устройству.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Устанавливает соединение явно (например, для проверки доступности).
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Устройство отключено или недоступно.</exception>
        Task EnsureConnectedAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Закрывает соединение.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task DisconnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает регистры хранения (функция 0x03).
        /// </summary>
        /// <param name="address">Адрес первого регистра.</param>
        /// <param name="quantity">Число регистров.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Запрос не выполнен.</exception>
        Task<ushort[]> ReadHoldingRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Читает входные регистры (функция 0x04).
        /// </summary>
        /// <param name="address">Адрес первого регистра.</param>
        /// <param name="quantity">Число регистров.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Запрос не выполнен.</exception>
        Task<ushort[]> ReadInputRegistersAsync(ushort address, ushort quantity, CancellationToken cancellationToken = default);

        /// <summary>
        /// Записывает один регистр (функция 0x06).
        /// </summary>
        /// <param name="address">Адрес регистра.</param>
        /// <param name="value">Значение.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Запрос не выполнен.</exception>
        Task WriteSingleRegisterAsync(ushort address, ushort value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Записывает несколько регистров (функция 0x10).
        /// </summary>
        /// <param name="address">Адрес первого регистра.</param>
        /// <param name="data">Значения.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Запрос не выполнен.</exception>
        Task WriteMultipleRegistersAsync(ushort address, ushort[] data, CancellationToken cancellationToken = default);
    }
}
