using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.Modbus.Transport;

namespace Mvk.Modbus.Tests
{
    /// <summary>
    /// Тестовый транспорт: управляемый обработчик, задержка и контроль параллелизма.
    /// </summary>
    internal sealed class FakeTransport : IMvkTransport
    {
        private readonly Func<ModbusRequest, Task<ModbusResponse>> handler;
        private readonly TimeSpan delay;
        private int concurrentCalls;

        public FakeTransport(
            Func<ModbusRequest, Task<ModbusResponse>>? handler = null,
            TimeSpan? delay = null)
        {
            this.handler = handler ?? (_ => Task.FromResult(ModbusResponse.Success([0x0001])));
            this.delay = delay ?? TimeSpan.Zero;
        }

        public bool IsConnected { get; private set; }

        public int MaxConcurrentCalls { get; private set; }

        public bool ConnectFails { get; set; }

        public int ConnectCalls { get; private set; }

        public int ExecuteCalls { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            ConnectCalls++;

            if (ConnectFails)
            {
                throw new MvkModbusException("тестовый отказ подключения", MvkErrorKind.Transport, "fake");
            }

            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public async Task<ModbusResponse> ExecuteAsync(ModbusRequest request, CancellationToken cancellationToken)
        {
            ExecuteCalls++;

            int current = Interlocked.Increment(ref concurrentCalls);
            MaxConcurrentCalls = Math.Max(MaxConcurrentCalls, current);

            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                ModbusResponse response = await handler(request).ConfigureAwait(false);

                if (response.IsSuccess)
                {
                    IsConnected = true;
                }

                return response;
            }
            finally
            {
                Interlocked.Decrement(ref concurrentCalls);
            }
        }

        public void Dispose()
        {
            IsConnected = false;
        }
    }

    /// <summary>
    /// Фабрика тестовых транспортов с доступом ко всем созданным экземплярам.
    /// </summary>
    internal sealed class FakeTransportFactory : IMvkTransportFactory
    {
        private readonly Func<MvkDeviceSettings, FakeTransport> factory;

        public FakeTransportFactory(Func<MvkDeviceSettings, FakeTransport> factory)
        {
            this.factory = factory;
        }

        public List<FakeTransport> Created { get; } = [];

        public IMvkTransport Create(MvkDeviceSettings settings)
        {
            FakeTransport transport = factory(settings);
            Created.Add(transport);
            return transport;
        }
    }
}
