using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace Mvk.Testing
{
    /// <summary>
    /// Программируемый транспорт для тестов: обработчик ответов, журнал запросов,
    /// управляемое состояние соединения.
    /// </summary>
    public sealed class FakeModbusTransport : IMvkTransport
    {
        private readonly Func<ModbusRequest, ModbusResponse> handler;

        /// <summary>
        /// Создаёт тестовый транспорт.
        /// </summary>
        /// <param name="handler">
        /// Обработчик запросов; по умолчанию запись — успех, чтение — два нулевых регистра.
        /// </param>
        public FakeModbusTransport(Func<ModbusRequest, ModbusResponse>? handler = null)
        {
            this.handler = handler ?? (request => request.IsWrite
                ? ModbusResponse.WriteSuccess()
                : ModbusResponse.Success([0x0000, 0x0000]));
        }

        /// <summary>
        /// Журнал выполненных запросов.
        /// </summary>
        public List<ModbusRequest> Requests { get; } = [];

        /// <summary>
        /// Признак установленного соединения.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <inheritdoc/>
        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            ModbusResponse response = handler(request);

            if (response.IsSuccess)
            {
                IsConnected = true;
            }

            return Task.FromResult(response);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            IsConnected = false;
        }
    }

    /// <summary>
    /// Фабрика тестовых транспортов с доступом ко всем созданным экземплярам.
    /// </summary>
    public sealed class FakeModbusTransportFactory : IMvkTransportFactory
    {
        private readonly Func<MvkDeviceSettings, FakeModbusTransport> factory;

        /// <summary>
        /// Создаёт фабрику.
        /// </summary>
        /// <param name="factory">
        /// Фабрика транспорта для конкретного устройства; по умолчанию — одна общая реализация.
        /// </param>
        public FakeModbusTransportFactory(Func<MvkDeviceSettings, FakeModbusTransport>? factory = null)
        {
            this.factory = factory ?? (_ => new FakeModbusTransport());
        }

        /// <summary>
        /// Созданные транспорты.
        /// </summary>
        public List<FakeModbusTransport> Created { get; } = [];

        /// <inheritdoc/>
        public IMvkTransport Create(MvkDeviceSettings settings)
        {
            FakeModbusTransport transport = factory(settings);
            Created.Add(transport);
            return transport;
        }
    }
}
