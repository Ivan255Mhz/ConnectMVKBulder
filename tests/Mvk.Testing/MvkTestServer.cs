using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NModbus;
using NModbus.Data;

namespace Mvk.Testing
{
    /// <summary>
    /// Эмулятор Modbus-slave для тестов: TCP-сервер NModbus на loopback-адресе
    /// с регистрами в памяти.
    /// </summary>
    public sealed class MvkTestServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly IModbusSlaveNetwork network;
        private readonly SlaveDataStore dataStore;
        private readonly Task listenTask;

        /// <summary>
        /// Порт, на котором слушает эмулятор.
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// Идентификатор устройства Modbus (slave id).
        /// </summary>
        public byte SlaveId { get; }

        private MvkTestServer(TcpListener listener, byte slaveId)
        {
            this.listener = listener;
            SlaveId = slaveId;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            dataStore = new SlaveDataStore();

            ModbusFactory factory = new();
            network = factory.CreateSlaveNetwork(listener);
            network.AddSlave(factory.CreateSlave(slaveId, dataStore));

            listenTask = network.ListenAsync();
        }

        /// <summary>
        /// Запускает эмулятор.
        /// </summary>
        /// <param name="slaveId">Идентификатор устройства Modbus.</param>
        /// <param name="port">TCP-порт; 0 — свободный порт, выбранный операционной системой.</param>
        public static MvkTestServer Start(byte slaveId = 1, int port = 0)
        {
            TcpListener listener = new(IPAddress.Loopback, port);
            listener.Start();
            return new MvkTestServer(listener, slaveId);
        }

        /// <summary>
        /// Возвращает свободный TCP-порт на loopback-адресе.
        /// </summary>
        public static int GetFreePort()
        {
            TcpListener probe = new(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        /// <summary>
        /// Записывает значение регистра хранения.
        /// </summary>
        public void SetHoldingRegister(ushort address, ushort value)
        {
            dataStore.HoldingRegisters.WritePoints(address, [value]);
        }

        /// <summary>
        /// Записывает значения регистров хранения.
        /// </summary>
        public void SetHoldingRegisters(ushort address, params ushort[] values)
        {
            dataStore.HoldingRegisters.WritePoints(address, values);
        }

        /// <summary>
        /// Читает значение регистра хранения.
        /// </summary>
        public ushort GetHoldingRegister(ushort address)
        {
            return dataStore.HoldingRegisters.ReadPoints(address, 1)[0];
        }

        /// <summary>
        /// Читает значения регистров хранения.
        /// </summary>
        public ushort[] GetHoldingRegisters(ushort address, ushort count)
        {
            return dataStore.HoldingRegisters.ReadPoints(address, count);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                (network as IDisposable)?.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                listener.Stop();
            }
            catch (Exception)
            {
            }

            try
            {
                await listenTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }
    }
}
