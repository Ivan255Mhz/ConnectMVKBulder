using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mvk.Modbus;
using Mvk.Modbus.Configuration;
using Mvk.Modbus.Pool;
using Mvk.Modbus.Protocol;
using Mvk.Testing;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01DeviceTests
    {
        [Fact]
        public async Task ReadDeviceInfoAsync_DecodesAllFields()
        {
            await using MvkTestServer server = MvkTestServer.Start();
            SeedText(server, Mvk01RegisterMap.DeviceSerialNumber, "00004730", 4);
            SeedUInt32(server, Mvk01RegisterMap.FirmwareVersion, 0x01071400);
            SeedUInt32(server, Mvk01RegisterMap.SystemErrorIndicators, 0b0100_0001);
            SeedUInt32(server, Mvk01RegisterMap.Uptime, 161653);
            SeedUInt32(server, Mvk01RegisterMap.MemoryCardSize, 1024);
            server.SetHoldingRegisters(Mvk01RegisterMap.DspMd5, [0x0001, 0x0203, 0x0405, 0x0607, 0x0809, 0x0A0B, 0x0C0D, 0x0E0F]);
            SeedUInt32(server, Mvk01RegisterMap.Unit1ControlState, 0x14 | (1u << 10));
            SeedUInt32(server, Mvk01RegisterMap.Unit2ControlState, 0x11);
            server.SetHoldingRegisters(Mvk01RegisterMap.DspIndicators, [0x0000, 0x0003, 0x0000, 0x0004, 0x0000, 0x002A]);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Mvk01DeviceInfo info = await device.ReadDeviceInfoAsync();

            Assert.Equal("00004730", info.SerialNumber);
            Assert.Equal(new Mvk01FirmwareVersion(1, 7, 20, 0), info.FirmwareVersion);
            Assert.True(info.Errors.DspReceiveError);
            Assert.True(info.Errors.ConfigurationCrcError);
            Assert.Equal(TimeSpan.FromSeconds(161653), info.Uptime);
            Assert.Equal(1024, info.MemoryCardKilobytes);
            Assert.Equal("000102030405060708090a0b0c0d0e0f", info.DspMd5);
            Assert.Equal(Mvk01Zone.Yellow, info.Unit1State.Zone);
            Assert.True(info.Unit1State.EnteredYellowZone);
            Assert.Equal(Mvk01Zone.Green, info.Unit2State.Zone);
            Assert.Equal(3, info.DspIndicators.OverloadChannelMask);
            Assert.True(info.DspIndicators.IsChannelOverloaded(1));
            Assert.False(info.DspIndicators.IsChannelOverloaded(3));
            Assert.Equal(42u, info.DspIndicators.CalculationCounter);
        }

        [Fact]
        public async Task ReadChannelAsync_DecodesSnapshot()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            int baseAddress = Mvk01RegisterMap.ChannelData(1);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.SensorVoltageOffset, 7.82f);

            float[] spectrum = Enumerable.Range(1, Mvk01RegisterMap.SpectrumBands)
                .Select(index => index * 0.5f)
                .ToArray();
            SeedFloats(server, baseAddress + Mvk01RegisterMap.SpectrumOffset, spectrum);

            SeedFloat(server, baseAddress + Mvk01RegisterMap.PeakFactorOffset, 3.5f);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.RmsX25kHzOffset, 12.25f);

            float[] rms = Enumerable.Range(1, Mvk01RegisterMap.RmsSetCount * Mvk01RegisterMap.RmsSetValues)
                .Select(index => (float)index)
                .ToArray();
            SeedFloats(server, baseAddress + Mvk01RegisterMap.RmsSetOffset, rms);

            SeedFloat(server, baseAddress + Mvk01RegisterMap.DcComponentOffset, 0.125f);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelStateOffset, 1u | (1u << 10));

            float[] peaks = Enumerable.Range(0, Mvk01RegisterMap.EquivalentValuesCount)
                .Select(index => 100f + index)
                .ToArray();
            SeedFloats(server, baseAddress + Mvk01RegisterMap.EquivalentPeakOffset, peaks);

            float[] ranges = Enumerable.Range(0, Mvk01RegisterMap.EquivalentValuesCount)
                .Select(index => 200f + index)
                .ToArray();
            SeedFloats(server, baseAddress + Mvk01RegisterMap.EquivalentRangeOffset, ranges);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Mvk01Channel channel = await device.ReadChannelAsync(1);

            Assert.Equal(1, channel.Channel);
            Assert.Equal(7.82f, channel.SensorVoltage, 3);
            Assert.Equal(Mvk01RegisterMap.SpectrumBands, channel.Spectrum.Length);
            Assert.Equal(0.5f, channel.Spectrum[0], 6);
            Assert.Equal(21.5f, channel.Spectrum[^1], 6);
            Assert.Equal(3.5f, channel.PeakFactor, 6);
            Assert.Equal(12.25f, channel.RmsX25kHzAcceleration, 6);
            Assert.Equal(Mvk01RegisterMap.RmsSetCount, channel.RmsSets.Count);
            Assert.Equal("10…3000 Гц", channel.RmsSets[0].Name);
            Assert.Equal(1f, channel.RmsSets[0].Acceleration);
            Assert.Equal(2f, channel.RmsSets[0].Velocity);
            Assert.Equal(3f, channel.RmsSets[0].Displacement);
            Assert.Equal(22f, channel.RmsSets[7].Acceleration);
            Assert.Equal(0.125f, channel.DcComponent, 6);
            Assert.True(channel.State.Overload);
            Assert.True(channel.State.DcVoltageAboveMaximum);
            Assert.Equal(100f, channel.EquivalentPeakValues[0]);
            Assert.Equal(123f, channel.EquivalentPeakValues[^1]);
            Assert.Equal(200f, channel.EquivalentRangeValues[0]);
            Assert.Equal(223f, channel.EquivalentRangeValues[^1]);
        }

        [Fact]
        public async Task ReadTachoAsync_DecodesSnapshot()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            int baseAddress = Mvk01RegisterMap.TachoData(1);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.TachoFrequencyOffset, 17.13f);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.TachoAverageIncrementOffset, 0.1f);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.TachoSensorErrorOffset, 0);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.TachoTimestampOffset, 65536);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.TachoInputLevelOffset, 1);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.TachoDirectionOffset, 2);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.TachoTransitionsOffset, 12345);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Mvk01TachoMeasurement tacho = await device.ReadTachoAsync(1);

            Assert.Equal(17.13f, tacho.FrequencyHz, 2);
            Assert.Equal(0.1f, tacho.AverageIncrementHz, 6);
            Assert.False(tacho.SensorError);
            Assert.Equal(1.0, tacho.TimestampSeconds, 6);
            Assert.True(tacho.InputLevel);
            Assert.Equal(Mvk01RotationDirection.Tacho1ToTacho2, tacho.Direction);
            Assert.Equal(12345u, tacho.TransitionCount);
        }

        [Fact]
        public async Task ReadChannelConfigAsync_DecodesSettings()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            int baseAddress = Mvk01RegisterMap.ChannelConfig(2);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelTypeOffset, 0);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.ChannelSensorFactorOffset, 1.25f);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.ChannelOverloadThresholdOffset, 0.9f);
            SeedTextSwapped(server, baseAddress + Mvk01RegisterMap.ChannelUnitsOffset, "m/s2", 16);
            SeedTextSwapped(server, baseAddress + Mvk01RegisterMap.ChannelSerialOffset, "SN-42", 16);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelFilterX25kHzOffset, (uint)Mvk01FilterX25kHz.Hz10000);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelFilter1LowOffset, 5);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelFilter1HighOffset, 20);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelFilter2LowOffset, 3);
            SeedUInt32(server, baseAddress + Mvk01RegisterMap.ChannelFilter2HighOffset, 30);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.ChannelMaxDcVoltageOffset, 15.5f);
            SeedFloat(server, baseAddress + Mvk01RegisterMap.ChannelMinDcVoltageOffset, -15.5f);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            Mvk01ChannelConfig config = await device.ReadChannelConfigAsync(2);

            Assert.Equal(2, config.Channel);
            Assert.Equal(0, config.ChannelType);
            Assert.Equal(1.25f, config.SensorFactor, 6);
            Assert.Equal(0.9f, config.OverloadThreshold, 6);
            Assert.Equal("m/s2", config.Units);
            Assert.Equal("SN-42", config.SerialNumber);
            Assert.Equal(Mvk01FilterX25kHz.Hz10000, config.FilterX25kHz);
            Assert.Equal(5, config.Filter1LowBand);
            Assert.Equal(20, config.Filter1HighBand);
            Assert.Equal(3, config.Filter2LowBand);
            Assert.Equal(30, config.Filter2HighBand);
            Assert.Equal(15.5f, config.MaxDcVoltage, 6);
            Assert.Equal(-15.5f, config.MinDcVoltage, 6);
        }

        [Fact]
        public async Task ReadMeasurementByNumberAsync_ReturnsAllChannels()
        {
            await using MvkTestServer server = MvkTestServer.Start();

            int address = Mvk01RegisterMap.MeasurementByNumber(48);
            float[] expected = Enumerable.Range(0, 8).Select(index => 1.0f + index).ToArray();
            SeedFloats(server, address, expected);

            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            float[] values = await device.ReadMeasurementByNumberAsync(Mvk01MeasurementNumber.RmsX25kHzAcceleration);
            ushort[] raw = await device.ReadMeasurementRawByNumberAsync(48);

            Assert.Equal(expected, values);
            Assert.Equal(Mvk01RegisterMap.MeasurementByNumberRegisterCount, raw.Length);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        public async Task ReadChannelAsync_InvalidChannel_Throws(int channel)
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.ReadChannelAsync(channel));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public async Task ReadTachoAsync_InvalidTacho_Throws(int tacho)
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => device.ReadTachoAsync(tacho));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(185)]
        public async Task ReadMeasurementByNumberAsync_InvalidNumber_Throws(int number)
        {
            await using MvkTestServer server = MvkTestServer.Start();
            await using MvkDevicePool pool = CreatePool(server.Port);
            Mvk01Device device = new(pool.GetDevice("mvk-1"));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => device.ReadMeasurementByNumberAsync(number));
        }

        [Fact]
        public async Task SaveConfigurationAsync_WritesCommandAndReadsSuccessStatus()
        {
            (MvkDevicePool pool, FakeModbusTransport transport) = CreateFakePool(request =>
                request.IsWrite
                    ? ModbusResponse.WriteSuccess()
                    : ModbusResponse.Success(RegisterCodec.FromUInt32(Mvk01Codec.StatusSuccess, MvkByteOrder.BigEndian)));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01CommandResult result = await device.SaveConfigurationAsync();

                Assert.True(result.IsSuccess);
                Assert.Equal(0u, result.DeviceStatus);

                ModbusRequest write = Assert.Single(transport.Requests, request => request.IsWrite);
                Assert.Equal(Mvk01RegisterMap.SystemCommand, write.StartAddress);
                Assert.Equal(new ushort[] { 0x000F, 0x0001 }, write.WriteData);
            }
        }

        [Fact]
        public async Task ExecuteSystemCommandAsync_BadCommandStatus_ReturnsBadCommand()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(request =>
                request.IsWrite
                    ? ModbusResponse.WriteSuccess()
                    : ModbusResponse.Success(RegisterCodec.FromUInt32(Mvk01Codec.StatusBadCommand, MvkByteOrder.BigEndian)));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01CommandResult result = await device.RebootAsync();

                Assert.Equal(Mvk01CommandStatus.BadCommand, result.Status);
                Assert.False(result.IsSuccess);
            }
        }

        [Fact]
        public async Task ExecuteSystemCommandAsync_FlashError_ReturnsFlashError()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(request =>
                request.IsWrite
                    ? ModbusResponse.WriteSuccess()
                    : ModbusResponse.Success(RegisterCodec.FromUInt32(Mvk01Codec.StatusFlashError, MvkByteOrder.BigEndian)));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01CommandResult result = await device.SaveConfigurationAsync();

                Assert.Equal(Mvk01CommandStatus.FlashError, result.Status);
            }
        }

        [Fact]
        public async Task ExecuteSystemCommandAsync_PendingStatus_TimesOut()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(request =>
                request.IsWrite
                    ? ModbusResponse.WriteSuccess()
                    : ModbusResponse.Success(RegisterCodec.FromUInt32(0x12345678, MvkByteOrder.BigEndian)));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01CommandResult result = await device.ExecuteSystemCommandAsync(
                    Mvk01SystemCommand.SaveConfiguration,
                    TimeSpan.FromMilliseconds(300));

                Assert.Equal(Mvk01CommandStatus.Timeout, result.Status);
            }
        }

        [Fact]
        public async Task ExecuteSystemCommandAsync_WriteFails_ReturnsTransportError()
        {
            (MvkDevicePool pool, FakeModbusTransport _) = CreateFakePool(request =>
                request.IsWrite
                    ? ModbusResponse.Failure(new MvkModbusException("нет связи", MvkErrorKind.Transport, "mvk-1"))
                    : ModbusResponse.Success([0x0000, 0x0000]));

            await using (pool)
            {
                Mvk01Device device = new(pool.GetDevice("mvk-1"));

                Mvk01CommandResult result = await device.SaveConfigurationAsync();

                Assert.Equal(Mvk01CommandStatus.TransportError, result.Status);
                Assert.NotNull(result.Error);
            }
        }

        private static MvkDevicePool CreatePool(int port)
        {
            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions
                {
                    Port = port,
                    RequireEvenAddressAndQuantity = true,
                    AllowConfigurationWrite = true
                },
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return new MvkDevicePool(options);
        }

        private static (MvkDevicePool Pool, FakeModbusTransport Transport) CreateFakePool(
            Func<ModbusRequest, ModbusResponse> handler)
        {
            FakeModbusTransport transport = new(handler);
            FakeModbusTransportFactory factory = new(_ => transport);

            MvkModbusOptions options = new()
            {
                Defaults = new MvkDefaultsOptions
                {
                    RequireEvenAddressAndQuantity = true,
                    AllowConfigurationWrite = true
                },
                Pool = new MvkPoolOptions { ReconnectEnabled = false },
                Devices = [new MvkDeviceOptions { Id = "mvk-1", Host = "127.0.0.1" }]
            };

            return (new MvkDevicePool(options, factory, middlewares: []), transport);
        }

        private static void SeedFloat(MvkTestServer server, int address, float value)
        {
            server.SetHoldingRegisters((ushort)address, RegisterCodec.FromSingle(value, MvkByteOrder.BigEndian));
        }

        private static void SeedFloats(MvkTestServer server, int address, params float[] values)
        {
            ushort[] registers = new ushort[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                RegisterCodec.FromSingle(values[i], MvkByteOrder.BigEndian).CopyTo(registers, i * 2);
            }

            server.SetHoldingRegisters((ushort)address, registers);
        }

        private static void SeedUInt32(MvkTestServer server, int address, uint value)
        {
            server.SetHoldingRegisters((ushort)address, RegisterCodec.FromUInt32(value, MvkByteOrder.BigEndian));
        }

        private static void SeedText(MvkTestServer server, int address, string text, int registerCount)
        {
            server.SetHoldingRegisters((ushort)address, RegisterCodec.FromText(text, registerCount, Encoding.ASCII));
        }

        private static void SeedTextSwapped(MvkTestServer server, int address, string text, int registerCount)
        {
            server.SetHoldingRegisters((ushort)address, Mvk01Codec.EncodeTextSwapped(text, registerCount, Encoding.ASCII));
        }
    }
}
