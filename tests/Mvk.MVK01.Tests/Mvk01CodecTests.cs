using System.Text;
using Mvk.Modbus.Configuration;
using Xunit;

namespace Mvk.MVK01.Tests
{
    public sealed class Mvk01CodecTests
    {
        [Fact]
        public void DecodeFirmwareVersion_ParsesExampleFromDocumentation()
        {
            Mvk01FirmwareVersion version = Mvk01Codec.DecodeFirmwareVersion(0x01071400);

            Assert.Equal(new Mvk01FirmwareVersion(1, 7, 20, 0), version);
            Assert.Equal("1.7.20.0", version.ToString());
        }

        [Fact]
        public void DecodeSystemErrors_ParsesAllFlags()
        {
            Mvk01SystemErrors errors = Mvk01Codec.DecodeSystemErrors(0b0111_0001);

            Assert.True(errors.DspReceiveError);
            Assert.False(errors.SdCardError);
            Assert.True(errors.DspSendError);
            Assert.True(errors.ModbusInitializationError);
            Assert.True(errors.ConfigurationCrcError);
            Assert.False(errors.IsClean);
        }

        [Theory]
        [InlineData(0b0000_0010, true)]
        [InlineData(0b0000_0100, true)]
        [InlineData(0b0000_1000, true)]
        [InlineData(0b0000_0000, false)]
        public void DecodeSystemErrors_SdCardMask(uint raw, bool expected)
        {
            Assert.Equal(expected, Mvk01Codec.DecodeSystemErrors(raw).SdCardError);
        }

        [Fact]
        public void DecodeUnitState_ParsesZoneAndEvents()
        {
            Mvk01UnitState state = Mvk01Codec.DecodeUnitState(0x14 | (1u << 10) | (1u << 11));

            Assert.Equal(Mvk01Zone.Yellow, state.Zone);
            Assert.False(state.SensorFailureEvent);
            Assert.True(state.EnteredYellowZone);
            Assert.True(state.EnteredRedZone);
        }

        [Fact]
        public void DecodeChannelState_ParsesBits()
        {
            Mvk01ChannelState state = Mvk01Codec.DecodeChannelState(1u | (1u << 6) | (1u << 11));

            Assert.True(state.Overload);
            Assert.False(state.Buffer16HzNotFilled);
            Assert.True(state.Buffer512HzNotFilled);
            Assert.True(state.DcVoltageBelowMinimum);
            Assert.False(state.DcVoltageAboveMaximum);
        }

        [Theory]
        [InlineData(65536u, 1.0)]
        [InlineData(32768u, 0.5)]
        [InlineData(0u, 0.0)]
        public void DecodeTachoTimestampSeconds_ConvertsFromFraction(uint raw, double expected)
        {
            Assert.Equal(expected, Mvk01Codec.DecodeTachoTimestampSeconds(raw), 6);
        }

        [Fact]
        public void EncodeSystemCommand_UsesDeviceByteOrder()
        {
            Assert.Equal(new ushort[] { 0x000F, 0x0001 }, Mvk01Codec.EncodeSystemCommand(Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.BigEndian));
            Assert.Equal(new ushort[] { 0x0001, 0x000F }, Mvk01Codec.EncodeSystemCommand(Mvk01SystemCommand.SaveConfiguration, MvkByteOrder.Mixed));
        }

        [Fact]
        public void DecodeDspIndicators_ParsesThreeGroups()
        {
            Mvk01DspIndicators indicators = Mvk01Codec.DecodeDspIndicators(
                [0x0000, 0x0003, 0x0000, 0x0004, 0x0000, 0x002A],
                MvkByteOrder.BigEndian);

            Assert.Equal((byte)0x03, indicators.OverloadChannelMask);
            Assert.True(indicators.IsChannelOverloaded(1));
            Assert.True(indicators.IsChannelOverloaded(2));
            Assert.False(indicators.IsChannelOverloaded(3));
            Assert.Equal(4u, indicators.ConfigurationFlags);
            Assert.Equal(42u, indicators.CalculationCounter);
        }

        [Fact]
        public void DecodeFloats_ReadsAllValues()
        {
            float[] values = Mvk01Codec.DecodeFloats(
                [0x4145, 0x70A4, 0x0000, 0x0000],
                2,
                MvkByteOrder.BigEndian);

            Assert.Equal(12.34f, values[0], 4);
            Assert.Equal(0f, values[1]);
        }

        [Fact]
        public void FormatMd5_ReturnsLowerCaseHex()
        {
            byte[] hash = [0x00, 0x11, 0xAB, 0xCD];

            Assert.Equal("0011abcd", Mvk01Codec.FormatMd5(hash));
        }

        [Fact]
        public void DecodeText_TrimsPadding()
        {
            ushort[] registers = Mvk.Modbus.Protocol.RegisterCodec.FromText("00004730", 4, Encoding.ASCII);

            Assert.Equal("00004730", Mvk01Codec.DecodeText(registers, 8));
        }

        [Fact]
        public void DefaultTextEncoding_DecodesCyrillic()
        {
            Assert.Equal("м/с2", Mvk01Codec.DefaultTextEncoding.GetString([0xEC, 0x2F, 0xF1, 0x32]));
        }

        [Fact]
        public void DecodeTextSwapped_MatchesRealDeviceUnits()
        {
            // Сырые регистры единиц измерения канала 1 с реального МВК01: "м/с2" в cp1251.
            ushort[] registers = [0x32F1, 0x2FEC, 0x0000, 0x0000];

            string text = Mvk01Codec.DecodeTextSwapped(registers, 8);

            Assert.Equal("м/с2", text);
        }

        [Fact]
        public void EncodeTextSwapped_ProducesRealDeviceRegisters()
        {
            ushort[] registers = Mvk01Codec.EncodeTextSwapped("м/с2", 4);

            Assert.Equal(new ushort[] { 0x32F1, 0x2FEC, 0x0000, 0x0000 }, registers);
        }

        [Fact]
        public void SpectrumBands_ContainExpectedFrequencies()
        {
            Assert.Equal(43, Mvk01SpectrumBands.Count);
            Assert.Equal(0.63, Mvk01SpectrumBands.GetCenterFrequency(1), 6);
            Assert.Equal(50.0, Mvk01SpectrumBands.GetCenterFrequency(20), 6);
            Assert.Equal(10000.0, Mvk01SpectrumBands.GetCenterFrequency(43), 6);
            Assert.Equal("10 кГц", Mvk01SpectrumBands.FormatBand(43));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Mvk01SpectrumBands.GetCenterFrequency(44));
        }
    }
}
