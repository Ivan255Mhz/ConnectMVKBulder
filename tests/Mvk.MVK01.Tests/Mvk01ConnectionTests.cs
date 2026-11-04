using System;
using System.Threading;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Protocol;
using Mvk.MVK01.Polling;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01ConnectionTests
    {
        [Fact]
        public async Task ConnectAsync_Default_DisallowsConfigurationWrite()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            Assert.False(connection.Device.Settings.AllowConfigurationWrite);
            Assert.True(connection.Device.Settings.RequireEvenAddressAndQuantity);
        }

        [Fact]
        public async Task ConnectAsync_AllowConfigurationWrite_EnablesConfigurationWrite()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port,
                allowConfigurationWrite: true);

            Assert.True(connection.Device.Settings.AllowConfigurationWrite);
        }

        [Fact]
        public async Task WriteProtectedArea_Default_ThrowsValidation()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            MvkModbusException exception = await Assert.ThrowsAsync<MvkModbusException>(
                () => connection.Device.Device.WriteMultipleRegistersAsync(0x0100, [0x0001, 0x0002]));

            Assert.Equal(MvkErrorKind.Validation, exception.ErrorKind);
        }

        [Fact]
        public async Task WriteProtectedArea_WithWriteAccess_Succeeds()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port,
                allowConfigurationWrite: true);

            await connection.Device.Device.WriteMultipleRegistersAsync(0x0100, [0x0001, 0x0002]);

            Assert.Equal(new ushort[] { 0x0001, 0x0002 }, server.GetHoldingRegisters(0x0100, 2));
        }

        [Fact]
        public async Task SaveConfigurationAsync_Default_ReturnsValidationError()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            Mvk01CommandResult result = await connection.SaveConfigurationAsync();

            Assert.Equal(Mvk01CommandStatus.TransportError, result.Status);
            Assert.NotNull(result.Error);
            Assert.Equal(MvkErrorKind.Validation, result.Error!.ErrorKind);
        }

        [Fact]
        public async Task ReadValueAsync_ReturnsSelectedChannel()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedMeasurement(server, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f);

            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            float value = await connection.ReadValueAsync(
                Mvk01MeasurementNumber.Rms10To1000Acceleration,
                channel: 2);

            float[] values = await connection.ReadValuesAsync(
                Mvk01MeasurementNumber.Rms10To1000Acceleration);

            Assert.Equal(2f, value);
            Assert.Equal(new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f }, values);
        }

        [Fact]
        public async Task WatchAsync_EmitsSamples()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedMeasurement(server, 5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

            await using Mvk01Connection connection = await Mvk01Connection.ConnectAsync(
                "127.0.0.1",
                server.Port);

            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(2));
            Mvk01Sample? sample = null;

            await foreach (Mvk01Sample item in connection.WatchAsync(
                Mvk01MeasurementNumber.Rms10To1000Acceleration,
                channel: 1,
                interval: TimeSpan.FromMilliseconds(10),
                cts.Token))
            {
                sample = item;
                break;
            }

            Assert.NotNull(sample);
            Assert.Equal(5f, sample!.ChannelValue!.Value);
            Assert.Equal("127.0.0.1", sample.DeviceId);
        }

        private static void SeedMeasurement(MvkTestServer server, params float[] values)
        {
            ushort[] registers = new ushort[values.Length * 2];

            for (int index = 0; index < values.Length; index++)
            {
                RegisterCodec
                    .FromSingle(values[index], MvkByteOrder.BigEndian)
                    .CopyTo(registers, index * 2);
            }

            server.SetHoldingRegisters(
                Mvk01RegisterMap.MeasurementByNumber(
                    (int)Mvk01MeasurementNumber.Rms10To1000Acceleration),
                registers);
        }
    }
}
