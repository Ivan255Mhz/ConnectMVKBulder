using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.Testing;
using Xunit;

namespace Mvk.Modbus.Tests.Pool
{
    public sealed class MvkDeviceTests
    {
        [Fact]
        public async Task ReadHoldingRegisters_AgainstEmulator_ReturnsData()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            server.SetHoldingRegisters(0x1004, 0x0100, 0x4301);

            await using MvkDevicePool pool = CreateEmulatorPool(server.Port);
            IMvkDevice device = pool.GetDevice("mvk-1");

            ushort[] data = await device.ReadHoldingRegistersAsync(0x1004, 2);

            Assert.Equal(new ushort[] { 0x0100, 0x4301 }, data);
            Assert.Equal(MvkDeviceState.Connected, device.State);
            Assert.True(device.IsConnected);
            Assert.Null(device.LastError);
            Assert.True(device.LastRequestAt > DateTimeOffset.MinValue);
        }

        [Fact]
        public async Task WriteSingleRegister_AgainstEmulator_UpdatesServer()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            await using MvkDevicePool pool = CreateEmulatorPool(
                server.Port,
                defaults: options => options.WritePolicy = MvkWritePolicy.AllowAll);
            IMvkDevice device = pool.GetDevice("mvk-1");

            await device.WriteSingleRegisterAsync(0x0002, 0x0001);

            Assert.Equal(0x0001, server.GetHoldingRegister(0x0002));
        }

        [Fact]
        public async Task Read_TransportFailure_ThrowsMvkModbusException()
        {
            int freePort = MvkTestServer.GetFreePort();

            await using MvkDevicePool pool = CreateEmulatorPool(
                freePort,
                defaults: options =>
                {
                    options.ConnectTimeoutMs = 300;
                    options.MaxRetries = 0;
                });

            IMvkDevice device = pool.GetDevice("mvk-1");

            MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
                () => device.ReadHoldingRegistersAsync(0x1004, 2));

            Assert.True(exception.IsTransient);
            Assert.Equal(MvkDeviceState.Faulted, device.State);
            Assert.NotNull(device.LastError);
        }

        [Fact]
        public async Task Requests_Serialized_WhenMaxConcurrentIsOne()
        {
            FakeTransport transport = new(delay: TimeSpan.FromMilliseconds(80));
            await using MvkDevicePool pool = CreateFakePool(
                defaults: options => options.MaxConcurrentRequests = 1,
                factory: _ => transport);

            IMvkDevice device = pool.GetDevice("mvk-1");

            await Task.WhenAll(
                device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2)),
                device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2)));

            Assert.Equal(1, transport.MaxConcurrentCalls);
        }

        [Fact]
        public async Task Requests_Parallel_WhenMaxConcurrentIsTwo()
        {
            FakeTransport transport = new(delay: TimeSpan.FromMilliseconds(80));
            await using MvkDevicePool pool = CreateFakePool(
                defaults: options => options.MaxConcurrentRequests = 2,
                factory: _ => transport);

            IMvkDevice device = pool.GetDevice("mvk-1");

            await Task.WhenAll(
                device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2)),
                device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2)));

            Assert.Equal(2, transport.MaxConcurrentCalls);
        }

        [Fact]
        public async Task EnsureConnected_RaisesStateChanged()
        {
            FakeTransport transport = new();
            await using MvkDevicePool pool = CreateFakePool(factory: _ => transport);

            IMvkDevice device = pool.GetDevice("mvk-1");
            List<MvkDeviceState> states = [];
            device.StateChanged += (_, args) => states.Add(args.NewState);

            await device.EnsureConnectedAsync();

            Assert.Equal(MvkDeviceState.Connected, device.State);
            Assert.Contains(MvkDeviceState.Connecting, states);
            Assert.Contains(MvkDeviceState.Connected, states);
        }

        [Fact]
        public async Task BackgroundMaintenance_ReconnectsFaultedDevice()
        {
            bool transportAvailable = false;
            FakeTransport transport = new(_ => transportAvailable
                ? Task.FromResult(ModbusResponse.Success([0x0001]))
                : Task.FromResult(ModbusResponse.Failure(
                    new MvkModbusException("нет связи", MvkErrorKind.Transport, "mvk-1"))));

            MvkModbusOptions options = new()
            {
                Pool = new MvkPoolOptions { ReconnectEnabled = true, HealthCheckIntervalSeconds = 1 },
                Defaults = new MvkDefaultsOptions
                {
                    ReconnectBaseDelayMs = 50,
                    ReconnectMaxDelayMs = 200
                },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            await using MvkDevicePool pool = new(options, new FakeTransportFactory(_ => transport), middlewares: []);
            IMvkDevice device = pool.GetDevice("mvk-1");

            await device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));
            Assert.Equal(MvkDeviceState.Faulted, device.State);

            transportAvailable = true;

            Assert.True(await WaitUntilAsync(
                () => device.State == MvkDeviceState.Connected,
                TimeSpan.FromSeconds(5)));
            Assert.True(device.IsConnected);
        }

        [Fact]
        public async Task Maintain_DisconnectsIdleConnection()
        {
            FakeTransport transport = new();
            await using MvkDevicePool pool = CreateFakePool(
                defaults: options => options.IdleDisconnectSeconds = 1,
                factory: _ => transport);

            IMvkDevice device = pool.GetDevice("mvk-1");

            await device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));
            Assert.True(device.IsConnected);

            await Task.Delay(1200);
            await pool.MaintainAsync();

            Assert.False(device.IsConnected);
            Assert.Equal(MvkDeviceState.Disconnected, device.State);
        }

        [Fact]
        public async Task Maintenance_KeepsActiveConnection()
        {
            FakeTransport transport = new();
            await using MvkDevicePool pool = CreateFakePool(
                defaults: options => options.IdleDisconnectSeconds = 60,
                factory: _ => transport);

            IMvkDevice device = pool.GetDevice("mvk-1");

            await device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));
            await pool.MaintainAsync();

            Assert.True(device.IsConnected);
        }

        private static MvkDevicePool CreateEmulatorPool(
            int port,
            Action<MvkDefaultsOptions>? defaults = null)
        {
            MvkDefaultsOptions defaultsOptions = new()
            {
                Port = port,
                ConnectTimeoutMs = 2000,
                ReadTimeoutMs = 1000
            };
            defaults?.Invoke(defaultsOptions);

            MvkModbusOptions options = new()
            {
                Defaults = defaultsOptions,
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return new MvkDevicePool(options);
        }

        private static MvkDevicePool CreateFakePool(
            Action<MvkDefaultsOptions>? defaults = null,
            Func<MvkDeviceSettings, FakeTransport>? factory = null)
        {
            MvkDefaultsOptions defaultsOptions = new();
            defaults?.Invoke(defaultsOptions);

            MvkModbusOptions options = new()
            {
                Defaults = defaultsOptions,
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            FakeTransportFactory transportFactory = new(factory ?? (_ => new FakeTransport()));

            return new MvkDevicePool(options, transportFactory, middlewares: []);
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return condition();
        }
    }
}
