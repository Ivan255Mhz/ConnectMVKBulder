using System;
using System.Linq;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pipeline;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Xunit;

namespace Mvk.Modbus.Tests.Pool
{
    public sealed class MvkDevicePoolTests
    {
        [Fact]
        public async Task GetDevice_Configured_ReturnsSameInstance()
        {
            await using MvkDevicePool pool = CreatePool(
                devices: [new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1" }]);

            IMvkDevice first = pool.GetDevice("mvk-1");
            IMvkDevice second = pool.GetDevice("MVK-1");

            Assert.Same(first, second);
            Assert.Equal("mvk-1", first.Settings.Id);
        }

        [Fact]
        public async Task GetDevice_Unknown_ThrowsDeviceNotFound()
        {
            await using MvkDevicePool pool = CreatePool(devices: []);

            MvkModbusException exception = Assert.Throws<MvkModbusException>(() => pool.GetDevice("unknown"));

            Assert.Equal(MvkErrorKind.DeviceNotFound, exception.ErrorKind);
            Assert.Equal("unknown", exception.DeviceId);
        }

        [Fact]
        public async Task TryGetDevice_Unknown_ReturnsFalse()
        {
            await using MvkDevicePool pool = CreatePool(devices: []);

            Assert.False(pool.TryGetDevice("unknown", out IMvkDevice? _));
        }

        [Fact]
        public async Task Devices_MatchConfiguration()
        {
            await using MvkDevicePool pool = CreatePool(
                devices:
                [
                    new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1" },
                    new MvkDeviceOptions { Id = "mvk-2", Host = "10.0.0.2", Enabled = false }
                ]);

            Assert.Equal(2, pool.Devices.Count);
            Assert.Contains(pool.Devices, device => device.Settings.Id == "mvk-2" && !device.Settings.Enabled);
        }

        [Fact]
        public async Task AddDevice_AddsAndRejectsDuplicate()
        {
            await using MvkDevicePool pool = CreatePool(devices: []);

            IMvkDevice added = pool.AddDevice(new MvkDeviceOptions { Id = "mvk-9", Host = "10.0.0.9" });

            Assert.Same(added, pool.GetDevice("mvk-9"));

            MvkModbusException exception = Assert.Throws<MvkModbusException>(
                () => pool.AddDevice(new MvkDeviceOptions { Id = "MVK-9", Host = "10.0.0.10" }));

            Assert.Equal(MvkErrorKind.Configuration, exception.ErrorKind);
        }

        [Fact]
        public async Task RemoveDevice_RemovesAndDisposes()
        {
            FakeTransportFactory factory = new(_ => new FakeTransport());
            await using MvkDevicePool pool = new(
                CreateOptions(devices: [new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1" }]),
                factory,
                middlewares: []);

            IMvkDevice device = pool.GetDevice("mvk-1");

            Assert.True(pool.RemoveDevice("mvk-1"));
            Assert.False(pool.TryGetDevice("mvk-1", out _));
            Assert.False(pool.RemoveDevice("mvk-1"));
        }

        [Fact]
        public async Task DisabledDevice_Execute_ReturnsDeviceDisabledFailure()
        {
            await using MvkDevicePool pool = CreatePool(
                devices: [new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1", Enabled = false }],
                middlewares: []);

            IMvkDevice device = pool.GetDevice("mvk-1");

            ModbusResponse response = await device.ExecuteAsync(
                ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));

            Assert.False(response.IsSuccess);
            Assert.Equal(MvkErrorKind.DeviceDisabled, response.Error!.ErrorKind);
        }

        [Fact]
        public async Task DeviceStateChanged_EventForwardedToPool()
        {
            FakeTransportFactory factory = new(_ => new FakeTransport());
            await using MvkDevicePool pool = new(
                CreateOptions(devices: [new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1" }]),
                factory,
                middlewares: []);

            MvkDeviceStateChangedEventArgs? captured = null;
            pool.DeviceStateChanged += (_, args) => captured = args;

            IMvkDevice device = pool.GetDevice("mvk-1");
            await device.EnsureConnectedAsync();

            Assert.NotNull(captured);
            Assert.Equal(MvkDeviceState.Connected, captured!.NewState);
            Assert.Equal("mvk-1", captured.Device.Settings.Id);
        }

        [Fact]
        public async Task MaintainAsync_ReconnectsFaultedDevice()
        {
            bool transportAvailable = false;
            FakeTransportFactory factory = new(_ => new FakeTransport(request =>
                transportAvailable
                    ? Task.FromResult(ModbusResponse.Success([0x0001]))
                    : Task.FromResult(ModbusResponse.Failure(
                        new MvkModbusException("нет связи", MvkErrorKind.Transport, "mvk-1")))));

            MvkModbusOptions options = CreateOptions(devices: [new MvkDeviceOptions { Id = "mvk-1", Host = "10.0.0.1" }]);
            options.Defaults.ReconnectBaseDelayMs = 50;
            options.Defaults.ReconnectMaxDelayMs = 100;

            await using MvkDevicePool pool = new(options, factory, middlewares: []);

            IMvkDevice device = pool.GetDevice("mvk-1");

            await device.ExecuteAsync(ModbusRequest.ReadHoldingRegisters("mvk-1", 0x0000, 2));
            Assert.Equal(MvkDeviceState.Faulted, device.State);

            transportAvailable = true;
            await Task.Delay(150);
            await pool.MaintainAsync();

            Assert.Equal(MvkDeviceState.Connected, device.State);
            Assert.True(device.IsConnected);
        }

        private static MvkModbusOptions CreateOptions(
            MvkDeviceOptions[] devices,
            bool reconnectEnabled = false)
        {
            return new MvkModbusOptions
            {
                Pool = new MvkPoolOptions { ReconnectEnabled = reconnectEnabled, HealthCheckIntervalSeconds = 1 },
                Devices = devices.ToList()
            };
        }

        private static MvkDevicePool CreatePool(
            MvkDeviceOptions[] devices,
            IModbusMiddleware[]? middlewares = null)
        {
            return new MvkDevicePool(
                CreateOptions(devices),
                new FakeTransportFactory(_ => new FakeTransport()),
                middlewares);
        }
    }
}
