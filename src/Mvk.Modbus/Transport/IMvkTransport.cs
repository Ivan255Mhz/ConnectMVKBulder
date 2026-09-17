using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Protocol;

namespace Mvk.Modbus.Transport
{
    /// <summary>
    /// Транспорт соединения с одним Modbus-устройством.
    /// </summary>
    public interface IMvkTransport : IDisposable
    {
        /// <summary>
        /// Признак установленного соединения.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Устанавливает соединение с устройством.
        /// </summary>
        /// <param name="cancellationToken">Токен отмены.</param>
        /// <exception cref="MvkModbusException">Ошибка подключения (Transport/Timeout).</exception>
        Task ConnectAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Закрывает соединение с устройством.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Выполняет запрос; при необходимости автоматически подключается.
        /// Транспортные ошибки возвращаются как <see cref="ModbusResponse.Failure(MvkModbusException)"/>,
        /// отмена вызывающего кода выбрасывает <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <param name="request">Запрос к устройству.</param>
        /// <param name="cancellationToken">Токен отмены.</param>
        Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken);
    }
}
