using System;
using Microsoft.Extensions.Logging;
using Mvk.Modbus.Configuration;

namespace Mvk.Modbus.Transport
{
    /// <summary>
    /// Создаёт транспорты <see cref="NModbusTransport"/> для устройств пула.
    /// </summary>
    public sealed class NModbusTransportFactory : IMvkTransportFactory
    {
        private readonly ILoggerFactory loggerFactory;

        /// <summary>
        /// Создаёт фабрику транспортов.
        /// </summary>
        /// <param name="loggerFactory">Фабрика логгеров (необязательна).</param>
        public NModbusTransportFactory(ILoggerFactory? loggerFactory = null)
        {
            this.loggerFactory = loggerFactory ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        }

        /// <inheritdoc/>
        public IMvkTransport Create(MvkDeviceSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return new NModbusTransport(settings, loggerFactory.CreateLogger<NModbusTransport>());
        }
    }
}
